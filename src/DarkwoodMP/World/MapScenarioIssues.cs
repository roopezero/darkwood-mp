using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for the bugs reported on GitHub, one or more steps each (see MapScenario;
    /// the order they run in is tools/tests/map_scenario_issues.py).
    ///
    /// Things these steps add to the test area are found again by where they are, in both games: the station's
    /// egg and infection patch are the ones nearest the centre, the extra ones the nearest to a fixed offset from
    /// it (<see cref="Spot"/>), far enough from the stations not to be mixed up with them.
    /// </summary>
    public static class MapScenarioIssues
    {
        // Where the extra things go, from the centre (the stations are within ~500 of it; the dump reaches 2000).
        private static readonly Dictionary<string, Vector3> Spots = new Dictionary<string, Vector3>
        {
            ["egg:near"] = new Vector3(0f, 0f, -900f),
            ["egg:far"] = new Vector3(2800f, 0f, 0f),   // outside what the client has loaded around it: culled there
            ["infection:client"] = new Vector3(-900f, 0f, 0f),
            ["infection:keep"] = new Vector3(900f, 0f, 0f),
        };
        private static Vector3 Spot(string which) => which == "station" || !Spots.TryGetValue(which, out var off) ? Centre : Centre + off;

        public static void Register()
        {
            // #1 loading screen while joining
            MapScenario.Add("join-log", JoinLog, timeout: 15f);
            // #13 a joiner placed off the others, not on top of them
            MapScenario.Add("join-apart", JoinApart, timeout: 20f);
            // #2 dead enemy's sounds, #3 re-equip after join (and an attack with it)
            MapScenario.Add("hold", Hold);
            MapScenario.Add("check-equip", CheckEquip);
            MapScenario.Add("spawn-dog", SpawnDog);
            MapScenario.Add("attack-dog", AttackDog, timeout: 25f);
            MapScenario.Add("dog-hurt", DogHurt);
            MapScenario.Add("kill-dog", KillDog);
            MapScenario.Add("dead-dog", DeadDog, timeout: 20f);
            MapScenario.Add("search-corpse", SearchCorpse, timeout: 20f);
            // #4 workbench storage
            MapScenario.Add("wb-fill", WbFill);
            MapScenario.Add("wb-use", WbUse, timeout: 30f);
            MapScenario.Add("wb-host", WbHost);
            // #5 larva eggs
            MapScenario.Add("issue-setup", Setup);
            MapScenario.Add("egg-break", EggBreak, timeout: 30f);
            MapScenario.Add("egg-check", EggCheck, timeout: 15f);
            // "egg-cull WHICH on|off": that egg made inactive here (on) or active again, as the game culls what's far off.
            MapScenario.Add("egg-cull", (a, r) =>
            {
                var e = Egg(a[0]);
                if (e == null) r.Fail("no egg");
                else { e.gameObject.SetActive(a.Length > 1 && a[1] == "off"); r.Info = $"{a[0]} egg {ObjRef.Of(e.gameObject)} active {e.gameObject.activeSelf}"; }
                return Nothing();
            });
            // "flags": the story flags here (to tell which one differs between the games).
            MapScenario.Add("flags", (a, r) =>
            {
                var dict = AccessTools.FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>("flagsDict")(Singleton<Flags>.Instance);
                r.Info = string.Join(",", dict.Values.Where(f => f != null).Select(f => f.name + (f.isTrue ? "=1" : "=0")).OrderBy(x => x, StringComparer.Ordinal));
                return Nothing();
            });
            // #6 infection
            MapScenario.Add("burn-infection", BurnInfection, timeout: 15f);
            MapScenario.Add("swing-infection", SwingInfection, timeout: 25f);
            MapScenario.Add("infection-check", InfectionCheck, timeout: 15f);
            // Where the patches are, noted in each game before any is cleared (a cleared one is removed).
            MapScenario.Add("infection-note", (a, r) => { foreach (var w in new[] { "station", "infection:client", "infection:keep" }) { var i = Patch(w); if (i != null) _noted[w] = i.transform.position; } r.Info = string.Join("; ", _noted.Select(kv => $"{kv.Key} {Fmt(kv.Value)}")); return Nothing(); });

            StateDump.AddSection("infection", ctx =>
            {
                foreach (var inf in StateDump.All<Infection>())
                {
                    if (inf == null || inf.disappearing || !ctx.Near(inf.transform.position)) continue; // a cleared one is gone
                    ctx.Add(inf.gameObject);
                }
            });
        }

        // ---- helpers ----------------------------------------------------------------------------------------------

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");

        /// <summary>This game's BepInEx log, as written so far (the disk log is flushed every second or two).</summary>
        internal static List<string> LogLines()
        {
            // BepInEx writes the file from a buffer it flushes every 2 s: without this, a line logged just before a
            // step was often missing (join-waited found no "World ready" after a quick join).
            foreach (var l in BepInEx.Logging.Logger.Listeners)
                if (l is BepInEx.Logging.DiskLogListener disk)
                    try { lock (disk.LogWriter) disk.LogWriter.Flush(); } catch { }
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
                return sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        }

        /// <summary>Wait (up to secs) for a line matching each pattern in this game's log after line `from`; the ones not found.</summary>
        private static IEnumerator WaitLog(int from, string[] patterns, float secs, List<string> missing)
        {
            float until = Time.realtimeSinceStartup + secs;
            while (true)
            {
                var lines = LogLines();
                missing.Clear();
                foreach (var pat in patterns)
                    if (!lines.Skip(from).Any(l => Regex.IsMatch(l, pat))) missing.Add(pat);
                if (missing.Count == 0 || Time.realtimeSinceStartup > until) yield break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        internal static int LogMark() { try { return LogLines().Count - 1; } catch { return 0; } }

        private static IEnumerable<InvItemClass> Carried(Player p) =>
            p.Inventory.slots.Concat(p.Hotbar.slots).Where(s => s != null && !InvItemClass.isNull(s.invItem)).Select(s => s.invItem);

        private static bool InBags(Player p, InvItemClass item) =>
            p.Inventory.slots.Concat(p.Hotbar.slots).Any(s => s != null && s.invItem == item);

        /// <summary>The nearest spot to `at` with nothing solid on it (as TestWorld's stations are placed).</summary>
        private static Vector3 FreeSpot(Vector3 at)
        {
            for (int ring = 0; ring < 12; ring++)
                for (int i = 0; i < Math.Max(1, ring * 6); i++)
                {
                    float a = i * Mathf.PI * 2f / Math.Max(1, ring * 6);
                    var p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * 50f;
                    if (!Physics.OverlapSphere(p, 60f, ~0, QueryTriggerInteraction.Ignore).Any(c => c.attachedRigidbody == null || !c.attachedRigidbody.CompareTag("Player")))
                        return p;
                }
            return at;
        }

        private static GameObject Place(string prefab, Vector3 at)
        {
            var go = Core.AddPrefab(prefab, FreeSpot(at), Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
            if (go != null) Core.addToSaveable(go, isDynamic: true, assignID: true);
            return go;
        }

        /// <summary>Things of a kind in the scene (culled ones too), nearest a spot.</summary>
        private static T NearestAll<T>(Vector3 to, Func<T, bool> ok) where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().Where(t => t != null && t.gameObject.scene.IsValid() && ok(t))
                .OrderBy(t => StateDump.Flat(t.transform.position - to)).FirstOrDefault();

        private static Item Egg(string which) => NearestAll<Item>(Spot(which == "station" ? which : "egg:" + which), i => i.gameObject.name.StartsWith("larva_egg", StringComparison.Ordinal));

        private static Infection Patch(string which, bool any = false) => NearestAll<Infection>(Spot(which), i => any || !i.disappearing);

        /// <summary>Swing what our player holds, as the game's attack animation does (Player.attack), facing `at`.</summary>
        private static readonly System.Reflection.MethodInfo AttackMethod = AccessTools.Method(typeof(Player), "attack", new[] { typeof(float) });
        private static void Swing(Player p, Vector3 at)
        {
            var d = at - p.transform.position; d.y = 0f;
            if (d.sqrMagnitude > 1f) p.transform.rotation = Quaternion.Euler(90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
            AsPlayer(() => AttackMethod.Invoke(p, new object[] { 1f }));
        }

        private static string ItemName(InvItemClass i) => InvItemClass.isNull(i) ? "nothing" : i.type;

        // ---- #1 loading screen while joining ------------------------------------------------------------------

        /// <summary>"join-log" (client): the join showed our loading screen, its stages, and handed over to the game's.</summary>
        private static IEnumerator JoinLog(string[] a, Result r)
        {
            var missing = new List<string>();
            yield return WaitLog(0, new[] { @"\[join\] loading screen shown", @"\[join\] loading screen: (download|load|connect|wait)\w* \d+%", @"\[join\] loading screen: (handed over to the game's|the game's loading has it now)" }, 6f, missing);
            var lines = LogLines();
            var join = lines.Where(l => l.Contains("[join]")).Select(l => l.Substring(l.IndexOf("[join]", StringComparison.Ordinal))).ToList();
            var bad = join.FirstOrDefault(l => l.Contains("didn't show") || l.Contains("hidden:"));
            r.Info = $"{join.Count} [join] lines: " + string.Join(" | ", join.Distinct().Take(6));
            if (missing.Count > 0) r.Fail($"not in the log: {string.Join(", ", missing)} (saw: {string.Join(" | ", join.Distinct().Take(8))})");
            else if (bad != null) r.Fail($"the loading screen fell back: {bad}");
        }

        // ---- #13 joining players placed apart ------------------------------------------------------------------

        /// <summary>
        /// "join-apart" (client, just after joining): our player isn't overlapping any other player, the log says
        /// where it was placed ([spawn]), and the others' puppets are seen where they are.
        /// </summary>
        private static IEnumerator JoinApart(string[] a, Result r)
        {
            var p = Player.Instance;
            if (p == null) { r.Fail("no player"); yield break; }
            var ps = Sync.PlayerSync.Instance;
            float until = Time.realtimeSinceStartup + 5f;
            while ((ps == null || !ps.Remotes.Values.Any(x => x.Owner != Sync.PlayerSync.DummyId && x.Last != null)) && Time.realtimeSinceStartup < until)
                yield return null;
            float apart = Sync.SpawnSpot.Apart(p) - 10f; // touching is fine, overlapping isn't
            var parts = new List<string>();
            if (ps != null)
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Owner == Sync.PlayerSync.DummyId || rp.Last == null) continue;
                    var o = rp.Last.Position; var me = p.transform.position;
                    float d = new Vector2(o.x - me.x, o.z - me.z).magnitude;
                    parts.Add($"{rp.Name} {d:0} away");
                    if (d < apart) r.Fail($"we overlap {rp.Name}: {d:0} apart, bodies need {apart:0}");
                }
            if (parts.Count == 0) r.Fail("no other player seen");
            // The log file is written a little behind (this step runs right after the join): wait for the line.
            var missing = new List<string>();
            yield return WaitLog(0, new[] { @"\[spawn\]" }, 8f, missing);
            var spawn = LogLines().LastOrDefault(l => l.Contains("[spawn]"));
            if (spawn == null) r.Fail("no [spawn] line in the log");
            r.Info = string.Join(", ", parts) + (spawn != null ? " | " + spawn.Substring(spawn.IndexOf("[spawn]", StringComparison.Ordinal)) : "");
        }

        // ---- #3 re-equip after join ----------------------------------------------------------------------------

        /// <summary>"hold TYPE" (host, before the join): TYPE in a hotbar slot, that slot selected and in our hands.</summary>
        private static IEnumerator Hold(string[] a, Result r)
        {
            var p = Player.Instance;
            string type = a.Length > 0 ? a[0] : "pitchfork";
            int slot = p.Hotbar.slots.FindIndex(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == type);
            if (slot < 0)
            {
                slot = p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
                if (slot < 0) slot = 0;
                var inBag = p.Inventory.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == type);
                var it = inBag != null ? new InvItemClass(inBag.invItem) : null;
                if (inBag != null) inBag.removeItem();
                if (!InvItemClass.isNull(p.Hotbar.slots[slot].invItem)) p.Hotbar.slots[slot].removeItem();
                p.Hotbar.slots[slot].inventory = p.Hotbar;
                if (it != null) p.Hotbar.slots[slot].createItem(it); else p.Hotbar.slots[slot].createItem(type, 1);
            }
            // Inventory.selectSlot does nothing for the slot that's already selected (an empty slot 0, say, which
            // we just filled), so select it the way Characters.Reequip does: InvSlot.select, through switchToItem.
            if (p.Hotbar.slots[slot].selected) p.selectCurrentHotbarSlot();
            else p.Hotbar.selectSlot(slot, noiseless: true, force: true);
            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem) || p.currentItem.type != type) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(0.5f);
            r.Info = $"hotbar slot {slot}: {ItemName(p.Hotbar.slots[slot].invItem)}, holding {ItemName(p.currentItem)}";
            if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != type) r.Fail(r.Info);
        }

        /// <summary>"check-equip" (client): what we hold is the selected hotbar slot's item (bare hands if it's empty),
        /// not the host's item from the save, and the log says it was re-equipped.</summary>
        private static IEnumerator CheckEquip(string[] a, Result r)
        {
            var p = Player.Instance;
            var missing = new List<string>();
            yield return WaitLog(0, new[] { @"\[chars\] re-equipped" }, 3f, missing);
            var slot = p.getCurrentlySelectedHotbarSlot();
            var want = slot != null && !InvItemClass.isNull(slot.invItem) ? slot.invItem : p.fists;
            var held = p.currentItem;
            string line = LogLines().LastOrDefault(l => l.Contains("[chars] re-equipped"));
            r.Info = $"holding {ItemName(held)}{(held == p.fists ? " (bare hands)" : "")}, selected slot holds {ItemName(slot?.invItem)}; {line?.Substring(line.IndexOf("[chars]", StringComparison.Ordinal))}";
            if (missing.Count > 0) r.Fail("no '[chars] re-equipped' in the log");
            else if (held != want && !(InvItemClass.isNull(held) && want == p.fists))
                r.Fail($"holding {ItemName(held)} ({(InBags(p, held) ? "in our bags" : "in no slot: the host's from the save")}), not {ItemName(want)}");
        }

        // ---- #2 dead enemy's sounds (and #3's attack) ----------------------------------------------------------

        private static Character _dog;       // host: the dog spawn-dog made
        private static float _dogHealth;     // host: its health then
        private static uint _dogId;          // client: the copy attack-dog went for
        private static string _dogDeath;     // client: its death cry's audio ID

        private static Character NetChar(uint id)
        {
            var ne = NetEnemies.Instance;
            if (ne == null) return null;
            return Object.FindObjectsOfType<Character>().FirstOrDefault(c => ne.NetIdOf(c, out uint i) && i == id);
        }

        /// <summary>"spawn-dog" (host): a Dog beside the centre, not set on anyone.</summary>
        private static IEnumerator SpawnDog(string[] a, Result r)
        {
            // Clear of the stations: a dog among them walked into a trap and died before anyone hit it.
            var spot = FreeSpot(Centre + new Vector3(0f, 0f, 1200f));
            // spawnCharacterAround takes a non-zero offset times the distance as the position itself.
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(Player.Instance.gameObject, spot, 1f, a.Length > 0 ? a[0] : "Dog", nocturnal: false, attackPlayer: false);
            if (c == null) { r.Fail("couldn't spawn a dog"); yield break; }
            c.transform.position = spot;
            _dog = c;
            yield return new WaitForSeconds(1.5f);
            _dogHealth = c.health; // once it's set up (it starts at 0)
            NetEnemies.Instance.NetIdOf(c, out uint id);
            r.Info = $"{c.name} #{id} at {Fmt(c.transform.position)}, health {c.health}";
        }

        /// <summary>"attack-dog" (client): walk to the host's dog and swing what we hold at it a few times.</summary>
        private static IEnumerator AttackDog(string[] a, Result r)
        {
            var p = Player.Instance;
            var ne = NetEnemies.Instance;
            Character dog = null;
            float until = Time.realtimeSinceStartup + 6f;
            while (dog == null && Time.realtimeSinceStartup < until)
            {
                // The one spawn-dog just made: by where it puts it, a copy made from its prefab (the host spawned it
                // while playing), then the newest net id there. A save dog or the far module's dog can be nearer
                // the centre; hitting one killed it instead.
                var spot = Centre + new Vector3(0f, 0f, 1200f);
                dog = Object.FindObjectsOfType<Character>().Where(c => c.alive && c.name.StartsWith("Dog") && ne != null && ne.NetIdOf(c, out _)
                                                                     && StateDump.Flat(c.transform.position - spot) < 400f)
                    .OrderBy(c => ne.MadeHere(c) ? 0 : 1)
                    .ThenByDescending(c => { ne.NetIdOf(c, out uint id); return id; }).FirstOrDefault();
                if (dog == null) yield return new WaitForSeconds(0.3f);
            }
            if (dog == null) { r.Fail("no copy of the host's dog here"); yield break; }
            ne.NetIdOf(dog, out _dogId);
            _dogDeath = dog.sounds != null ? dog.sounds.death : null;
            // A copy has its own idle loop and the host's relayed sounds while it lives: the death check below
            // is only worth something if there was something to stop.
            bool loud = dog.GetComponentsInChildren<AudioObject>(true).Any(ao => ao != null && ao.IsPlaying());
            // Darkwood has no bare-handed attack: a new character takes a weapon out first (the game's own hotbar
            // select), so the swing is a real one.
            string type = a.Length > 0 ? a[0] : "pitchfork";
            if (InvItemClass.isNull(p.currentItem))
            {
                var slot = p.getCurrentlySelectedHotbarSlot() ?? p.Hotbar.slots[0];
                if (!InvItemClass.isNull(slot.invItem)) slot = p.Hotbar.slots.FirstOrDefault(s => InvItemClass.isNull(s.invItem)) ?? slot;
                slot.inventory = p.Hotbar;
                if (InvItemClass.isNull(slot.invItem)) slot.createItem(type, 1);
                foreach (var s in p.Hotbar.slots) s.deselect();
                slot.select(noiseless: true);
                float t = Time.realtimeSinceStartup + 5f;
                while ((InvItemClass.isNull(p.currentItem) || p.switchingItem) && Time.realtimeSinceStartup < t) yield return null;
                if (InvItemClass.isNull(p.currentItem)) { r.Fail($"couldn't take the {type} out"); yield break; }
            }
            int swings = 0;
            for (int i = 0; i < 6 && dog != null && dog.alive; i++)
            {
                yield return Walk(dog.transform.position, 70f);
                Swing(p, dog.transform.position);
                swings++;
                yield return new WaitForSeconds(0.6f);
            }
            r.Info = $"{swings} swing(s) with {ItemName(p.currentItem)} at {dog.name} #{_dogId} (sounds playing on it before: {loud})";
        }

        /// <summary>"dog-hurt" (host): the client's swings took health off the dog here.</summary>
        private static IEnumerator DogHurt(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            if (_dog == null) { r.Fail("no dog (spawn-dog first)"); yield break; }
            r.Info = $"{_dog.name}: health {_dogHealth} -> {_dog.health}, alive {_dog.alive}";
            if (!(_dog.health < _dogHealth)) r.Fail((_dog.alive ? "the client's attack did nothing here: " : "it died of something else before the client's attack: ") + r.Info);
        }

        /// <summary>"kill-dog" (host): our player kills the dog.</summary>
        private static IEnumerator KillDog(string[] a, Result r)
        {
            var p = Player.Instance;
            if (_dog == null) { r.Fail("no dog"); yield break; }
            if (_dog.alive) AsPlayer(() => _dog.getHit(_dog.health + 500f, p.transform, false, true, false));
            yield return new WaitForSeconds(1f);
            r.Info = $"{_dog.name} alive {_dog.alive}";
            if (_dog.alive) r.Fail("it's still alive");
        }

        /// <summary>"dead-dog" (client): our copy of the dog is dead, and silent but for its death cry.</summary>
        private static IEnumerator DeadDog(string[] a, Result r)
        {
            int from = 0;
            var missing = new List<string>();
            yield return WaitLog(from, new[] { $@"\[enemy\] {_dogId} died: sounds stopped", $@"#{_dogId} died: its corpse can be searched here" }, 8f, missing);
            yield return new WaitForSeconds(2f); // past the death cry and any late sound from the host
            var dog = NetChar(_dogId);
            if (dog == null) { r.Fail($"the dog #{_dogId} is gone here"); yield break; }
            var snd = dog.GetComponent<CharacterSounds>();
            var loop = snd != null ? snd.idleAudioObject : null;
            var playing = dog.GetComponentsInChildren<AudioObject>(true).Where(ao => ao != null && ao.IsPlaying()).Select(ao => ao.audioID).ToList();
            var stray = playing.Where(id => id != _dogDeath).ToList();
            r.Info = $"{dog.name} #{_dogId} alive {dog.alive}, idle loop {(loop != null && loop.IsPlaying() ? loop.audioID : "off")}, playing [{string.Join(", ", playing)}], death cry {_dogDeath}";
            if (dog.alive) r.Fail($"it's alive here: {r.Info}");
            else if (missing.Count > 0) r.Fail($"not in the log: {string.Join(", ", missing)}");
            else if (loop != null && loop.IsPlaying()) r.Fail($"its idle loop {loop.audioID} still plays");
            else if (stray.Count > 0) r.Fail($"sounds still playing on the corpse: {string.Join(", ", stray)}");
        }

        /// <summary>"search-corpse" (client): open the dead dog's body and take what's in it (as take-item does).</summary>
        private static IEnumerator SearchCorpse(string[] a, Result r)
        {
            var p = Player.Instance;
            var dog = NetChar(_dogId);
            if (dog == null) { r.Fail($"the dog #{_dogId} is gone here"); yield break; }
            var item = dog.GetComponent<Item>();
            var inv = dog.GetComponentInChildren<Inventory>(true);
            if (item == null || inv == null) { r.Fail($"its corpse can't be searched here (Item {item != null}, Inventory {inv != null})"); yield break; }
            yield return Walk(dog.transform.position, 90f);
            int mark = LogMark();
            p.openedItemInventory = inv;
            yield return new WaitForSeconds(1.5f); // the host's contents come back (ContainerSlotsAll)
            var slot = inv.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem));
            string took = "nothing in it";
            if (slot != null)
            {
                var it = new InvItemClass(slot.invItem);
                AsPlayer(() => { slot.removeItem(); p.Inventory.addItemTypeToPlayer(it.type, it.amount, dropIfNoRoom: true); });
                took = $"took {it.type} x{it.amount}";
                yield return new WaitForSeconds(0.8f);
            }
            p.openedItemInventory = null;
            yield return new WaitForSeconds(1.5f);
            var refused = LogLines().Skip(mark).FirstOrDefault(l => l.Contains("refused"));
            r.Info = $"{dog.name} #{_dogId} {ObjRef.Of(inv.gameObject)}: {took}";
            if (refused != null) r.Fail($"{r.Info}; {refused}");
        }

        // ---- #4 workbench storage ------------------------------------------------------------------------------

        private static readonly string[] WbItems = { "nail", "wire", "rope" };

        private static int Amount(Inventory inv, string type) => inv.slots.Where(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == type).Sum(s => s.invItem.amount);

        private static Workbench Bench() => Nearest(Object.FindObjectsOfType<Workbench>().Where(w => w.normalInventory != null), Centre);

        /// <summary>"wb-fill" (host, before the join): put things in the workbench's storage, and change them again
        /// once a player has begun to join (while their game loads the snapshot, which doesn't have that change).</summary>
        private static IEnumerator WbFill(string[] a, Result r)
        {
            var p = Player.Instance;
            var wb = Bench();
            if (wb == null) { r.Fail("no workbench"); yield break; }
            var st = wb.normalInventory;
            yield return Walk(wb.transform.position);
            p.openedItemInventory2 = st;
            yield return new WaitForSeconds(0.6f);
            var free = Enumerable.Range(0, st.slots.Count).Where(i => InvItemClass.isNull(st.slots[i].invItem)).Take(WbItems.Length).ToList();
            if (free.Count < WbItems.Length) { p.openedItemInventory2 = null; r.Fail($"only {free.Count} free storage slots"); yield break; }
            for (int i = 0; i < WbItems.Length; i++) { st.slots[free[i]].inventory = st; st.slots[free[i]].createItem(WbItems[i], 3); }
            yield return new WaitForSeconds(0.6f);
            p.openedItemInventory2 = null;
            Plugin.Instance.StartCoroutine(WbLateChange(wb, free));
            r.Info = $"{st.name} {ObjRef.Of(st.gameObject)}: {string.Join(", ", WbItems.Select((t, i) => $"{free[i]}={t} x3"))}; a change follows once a player joins";
        }

        private static IEnumerator WbLateChange(Workbench wb, List<int> slots)
        {
            float until = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < until && (Net.Session.Instance == null || Net.Session.Instance.Peers.Count == 0)) yield return null;
            yield return new WaitForSecondsRealtime(2f); // the snapshot went with the Welcome; their game is loading it now
            var p = Player.Instance;
            if (wb == null || p == null) yield break;
            var st = wb.normalInventory;
            p.openedItemInventory2 = st;
            yield return new WaitForSeconds(0.6f);
            st.slots[slots[1]].removeItem();                          // wire gone
            st.slots[slots[2]].removeItem();
            st.slots[slots[2]].inventory = st; st.slots[slots[2]].createItem("rope", 5); // rope 3 -> 5
            yield return new WaitForSeconds(0.6f);
            p.openedItemInventory2 = null;
            Plugin.Log.LogInfo($"[map] wb-fill: changed the storage while a player joins (slot {slots[1]} emptied, slot {slots[2]} rope x5)");
        }

        /// <summary>"wb-use" (client): the storage is the host's (asked for once our world loaded); open the workbench as
        /// Workbench.open does, shift-click something in (InvSlot.transferItem) and one stack out; nothing refused or dropped.</summary>
        private static IEnumerator WbUse(string[] a, Result r)
        {
            var p = Player.Instance;
            var wb = Bench();
            if (wb == null) { r.Fail("no workbench"); yield break; }
            var st = wb.normalInventory;
            var sref = ObjRef.Of(st.gameObject);
            var missing = new List<string>();
            yield return WaitLog(0, new[] { Regex.Escape($"[slots] {sref} contents from host") }, 3f, missing);
            if (missing.Count > 0) { r.Fail($"no '[slots] {sref} contents from host' in the log: the storage wasn't asked for after joining"); yield break; }
            string got = LogLines().Last(l => l.Contains($"[slots] {sref} contents from host"));
            got = got.Substring(got.IndexOf("[slots]", StringComparison.Ordinal));
            string Contents() => string.Join(",", st.slots.Where(s => !InvItemClass.isNull(s.invItem)).Select(s => $"{s.invItem.type}x{s.invItem.amount}"));
            string before = Contents();
            if (st.slots.Any(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == "wire") || !st.slots.Any(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == "rope" && s.invItem.amount == 5))
            { r.Fail($"the storage here isn't the host's after its late change (wire gone, rope x5): {before}; {got}"); yield break; }

            int dropped0 = Object.FindObjectsOfType<Inventory>().Count(i => i.name.StartsWith("DroppedItem", StringComparison.Ordinal));
            yield return Walk(wb.transform.position);
            // What we carried before (the trade before this step can have bought a nail).
            int nails0 = Carried(p).Where(i => i.type == "nail").Sum(i => i.amount), bandages0 = Carried(p).Where(i => i.type == "bandages").Sum(i => i.amount);
            // Something of ours to put in (a new character has empty bags).
            var bag = p.Inventory.slots.FirstOrDefault(s => InvItemClass.isNull(s.invItem));
            if (bag == null) { r.Fail("no free slot in our bags"); yield break; }
            bag.inventory = p.Inventory;
            bag.createItem("bandages", 2);
            yield return new WaitForSeconds(0.5f);
            int mark = LogMark();
            AsPlayer(() => wb.open());
            yield return new WaitForSeconds(SlotShare.AskTimeout + 0.5f); // it asks the host again as it opens; changes wait for the answer
            if (p.openedItemInventory2 != st) { r.Fail("the workbench didn't open its storage"); yield break; }
            AsPlayer(() => bag.transferItem());                         // shift-click into the storage
            yield return new WaitForSeconds(0.8f);
            var nails = st.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == "nail");
            if (nails != null) AsPlayer(() => nails.transferItem());   // and one stack out
            yield return new WaitForSeconds(0.8f);
            try { p.closeInventory(); } catch (Exception e) { Plugin.Log.LogWarning($"[map] wb-use: closeInventory: {e.Message}"); }
            if (p.openedItemInventory2 == st) p.openedItemInventory2 = null;
            yield return new WaitForSeconds(1.5f);
            string after = Contents();
            int dropped1 = Object.FindObjectsOfType<Inventory>().Count(i => i.name.StartsWith("DroppedItem", StringComparison.Ordinal));
            var refused = LogLines().Skip(mark).Where(l => l.Contains("refused")).ToList();
            // A shift-click moves one of the stack (InvSlot.transferItem; transferItemAll is the whole stack).
            bool inStorage = Amount(st, "bandages") == 1 && Carried(p).Where(i => i.type == "bandages").Sum(i => i.amount) == bandages0 + 1;
            bool tookNails = Carried(p).Where(i => i.type == "nail").Sum(i => i.amount) == nails0 + 1 && Amount(st, "nail") == 2;
            r.Info = $"{sref} before [{before}] after [{after}]; {got}";
            if (refused.Count > 0) r.Fail($"refused: {refused[0]}");
            else if (!inStorage) r.Fail($"the bandages didn't go into the storage: {r.Info}");
            else if (!tookNails) r.Fail($"the nails didn't come into our bags: {r.Info}");
            else if (dropped1 != dropped0) r.Fail($"dropped items {dropped0} -> {dropped1}: {r.Info}");
        }

        /// <summary>"wb-host" (host): the client's two changes (a bandage in, a nail out) are in our storage, and we refused none of them.</summary>
        private static IEnumerator WbHost(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            var wb = Bench();
            if (wb == null) { r.Fail("no workbench"); yield break; }
            var st = wb.normalInventory;
            string contents = string.Join(",", st.slots.Where(s => !InvItemClass.isNull(s.invItem)).Select(s => $"{s.invItem.type}x{s.invItem.amount}"));
            var refused = LogLines().Where(l => l.Contains($"of {st.name}: refused")).ToList();
            r.Info = $"{st.name}: [{contents}]";
            if (refused.Count > 0) r.Fail($"refused: {refused[0]}");
            else if (Amount(st, "bandages") != 1 || Amount(st, "nail") != 2)
                r.Fail($"the client's changes aren't here: {r.Info}");
        }

        // ---- #5 larva eggs, #6 infection: the extra ones ------------------------------------------------------

        /// <summary>"issue-setup" (host, before the join): a second egg near the area and a third far off, and two more infection patches.</summary>
        private static IEnumerator Setup(string[] a, Result r)
        {
            var made = new List<string>();
            foreach (var kv in Spots)
            {
                string prefab = kv.Key.StartsWith("egg") ? "Objects/larva_egg_big" : "Traps/infection_splat";
                var go = Place(prefab, Centre + kv.Value);
                if (go == null) { r.Fail($"couldn't place {prefab}"); yield break; }
                made.Add($"{kv.Key} {ObjRef.Of(go)} at {Fmt(go.transform.position)}");
            }
            yield return new WaitForSeconds(0.5f);
            r.Info = string.Join("; ", made);
        }

        /// <summary>"egg-break station|near|far": our player hits that egg until it breaks (walking to it first).</summary>
        private static IEnumerator EggBreak(string[] a, Result r)
        {
            var p = Player.Instance;
            string which = a.Length > 0 ? a[0] : "station";
            var egg = Egg(which);
            if (egg == null) { r.Fail("no egg"); yield break; }
            var back = p.transform.position;
            yield return Walk(egg.transform.position, 100f);
            yield return new WaitForSeconds(0.5f);
            bool wasActive = egg.gameObject.activeInHierarchy;
            for (int i = 0; i < 10 && !egg.destroyed; i++)
            {
                AsPlayer(() => egg.getHit(2, p.transform, true));
                yield return new WaitForSeconds(0.3f);
            }
            r.Info = $"{which} egg {egg.gameObject.name} {ObjRef.Of(egg.gameObject)} at {Fmt(egg.transform.position)}: broken {egg.destroyed}, health {egg.health}";
            if (!egg.destroyed) { r.Fail(r.Info); yield break; }
            yield return new WaitForSeconds(1f);
            if (StateDump.Flat(back - p.transform.position) > 1000f) yield return Walk(back, 100f);
            if (!wasActive) r.Info += " (it was culled when we got there)";
        }

        /// <summary>"egg-check station,near,far [--from-other WHICH,...]": those eggs are broken here, and for each in
        /// --from-other the log says the other game's break arrived (and whether it was culled here then).</summary>
        private static IEnumerator EggCheck(string[] a, Result r)
        {
            var which = (a.Length > 0 ? a[0] : "station").Split(',');
            var fromOther = a.Length > 2 && a[1] == "--from-other" ? a[2].Split(',') : new string[0];
            var eggs = which.ToDictionary(w => w, Egg);
            var missing = new List<string>();
            var pats = fromOther.Where(eggs.ContainsKey).Where(w => eggs[w] != null)
                .Select(w => $@"\[world\] egg {Regex.Escape(ObjRef.Of(eggs[w].gameObject).ToString())} destroyed \(from ").ToArray();
            yield return WaitLog(0, pats, 6f, missing);
            var lines = LogLines();
            var parts = new List<string>();
            foreach (var kv in eggs)
            {
                var e = kv.Value;
                if (e == null) { r.Fail($"no {kv.Key} egg here"); continue; }
                string rf = ObjRef.Of(e.gameObject).ToString();
                var line = lines.LastOrDefault(l => l.Contains($"[world] egg {rf} destroyed"));
                parts.Add($"{kv.Key} {rf} broken {e.destroyed} active {e.gameObject.activeInHierarchy}" + (line != null ? $" '{line.Substring(line.IndexOf("[world]", StringComparison.Ordinal))}'" : ""));
                if (!e.destroyed) r.Fail($"the {kv.Key} egg {rf} isn't broken here");
            }
            r.Info = string.Join("; ", parts);
            if (missing.Count > 0) r.Fail($"not in the log: {string.Join(", ", missing)}; {r.Info}");
            else if (r.Failure != null) r.Failure += "; " + r.Info;
        }

        // ---- #6 infection --------------------------------------------------------------------------------------

        private static readonly Dictionary<string, Vector3> _noted = new Dictionary<string, Vector3>();
        private static IEnumerator Nothing() { yield break; }

        /// <summary>"burn-infection" (host): set the station's patch alight as fire does (a Burn on it, which clears every patch within 40).</summary>
        private static IEnumerator BurnInfection(string[] a, Result r)
        {
            var inf = Patch("station");
            if (inf == null) { r.Fail("no infection"); yield break; }
            yield return Walk(inf.transform.position, 150f);
            var near = Object.FindObjectsOfType<Infection>().Where(i => !i.disappearing && StateDump.Flat(i.transform.position - inf.transform.position) <= 40f).ToList();
            AsPlayer(() => inf.gameObject.AddComponent<Burn>().burnTime = 5f);
            yield return new WaitForSeconds(1f);
            r.Info = $"{inf.name} {ObjRef.Of(inf.gameObject)} at {Fmt(inf.transform.position)}: {near.Count(i => i == null || i.disappearing)} of {near.Count} patches cleared";
            if (near.Any(i => i != null && !i.disappearing)) r.Fail(r.Info);
        }

        /// <summary>"swing-infection" (client): our player holds a torch and swings it at the extra patch (MeleeSensor's
        /// flaming weapon clears it: Infection.disappear), as a request to the host.</summary>
        private static IEnumerator SwingInfection(string[] a, Result r)
        {
            var p = Player.Instance;
            string type = a.Length > 0 ? a[0] : "torch";
            var inf = Patch("infection:client");
            if (inf == null) { r.Fail("no patch"); yield break; }
            string rf = ObjRef.Of(inf.gameObject).ToString(), infAt = Fmt(inf.transform.position);
            var free = p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
            if (free < 0) free = 0;
            p.Hotbar.slots[free].inventory = p.Hotbar;
            if (!InvItemClass.isNull(p.Hotbar.slots[free].invItem)) p.Hotbar.slots[free].removeItem();
            p.Hotbar.slots[free].createItem(type, 1);
            // As Inventory.selectSlot does, but also when that slot is the selected one already (it then does nothing).
            foreach (var s in p.Hotbar.slots) s.deselect();
            p.Hotbar.slots[free].select(noiseless: true);
            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem) || p.currentItem.type != type || p.switchingItem) && Time.realtimeSinceStartup < until) yield return null;
            if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != type) { r.Fail($"couldn't take the {type} out (holding {ItemName(p.currentItem)})"); yield break; }
            var sensor = p.currentItem.baseClass.meleeSensor;
            bool flaming = sensor != null && sensor is GameObject g && g.GetComponent<MeleeSensor>() is MeleeSensor ms && ms.flaming;
            int mark = LogMark();
            string seen = "";
            // MeleeSensor hits a patch only if Core.canSeeAttack passes: rays from our player at a character's height
            // (14) towards the patch (at y -40), as long as the flat distance, must hit the patch's collider.
            // The sensor's check is against the collider it touched, which may be on a child of the patch.
            var hitT = inf.GetComponentInChildren<Collider>()?.transform ?? inf.transform;
            // The patch's collider is a ball of radius 70 around a point 50 above the patch itself, and a ray that
            // starts inside a collider doesn't hit it: so from closer than ~70 it never sees the patch. Stand just
            // outside it, where the torch still reaches into it.
            float[] stand = { 120f, 105f, 95f, 85f, 78f, 72f };
            var tried = new List<string>();
            for (int i = 0; i < 6 && !inf.disappearing; i++)
            {
                foreach (var d in stand)
                {
                    var off = p.transform.position - inf.transform.position; off.y = 0f;
                    if (off.sqrMagnitude < 1f) off = Vector3.right;
                    var spot = inf.transform.position + off.normalized * d; spot.y = p.transform.position.y;
                    yield return Walk(spot, 4f);
                    bool sees = Core.canSeeAttack(p.transform, hitT);
                    if (i == 0) tried.Add($"{StateDump.Flat(p.transform.position - inf.transform.position):0}:{(sees ? "sees" : "no")}");
                    if (sees) break;
                }
                if (i == 0)
                {
                    // What the game's ray (Core.canSeeAttack's first) runs into from here.
                    var from = new Vector3(p.transform.position.x, Core.getYpos(PosType.character, false), p.transform.position.z);
                    var to = new Vector3(hitT.position.x, hitT.position.y, hitT.position.z);
                    float len = StateDump.Flat(to - from);
                    var hits = Physics.RaycastAll(from, (to - from).normalized, len, 44165635);
                    var cc = hitT.GetComponent<Collider>();
                    tried.Add(FormattableString.Invariant($"queriesHitTriggers {Physics.queriesHitTriggers}, player y {p.transform.position.y:0.0}, ray y {from.y:0.0}, patch y {to.y:0.0} (inf {inf.transform.position.y:0.0}), collider {hitT.name} bounds {cc?.bounds.center} ext {cc?.bounds.extents}, ray {len:0.0} hits [{string.Join(",", hits.Select(h => $"{h.collider.name} L{h.collider.gameObject.layer}"))}]"));
                }
                Swing(p, inf.transform.position);
                yield return new WaitForFixedUpdate();
                var sensors = Object.FindObjectsOfType<MeleeSensor>().Where(m => m.type == MeleeSensor.MeleeSensorType.player).ToList();
                var col = inf.GetComponentsInChildren<Collider>(true);
                if (i == 0)
                    seen = $"; melee {p.currentItem.baseClass.isMelee}, {sensors.Count} sensor(s) " + string.Join(",", sensors.Select(m => $"{m.name} flaming {m.flaming} layer {m.gameObject.layer} ignores layer 1: {Physics.GetIgnoreLayerCollision(m.gameObject.layer, 1)}, overlaps {m.GetComponentsInChildren<Collider>().Any(mc => col.Any(ic => mc.bounds.Intersects(ic.bounds)))} at {StateDump.Flat(m.transform.position - inf.transform.position):0}, sees it {Core.canSeeAttack(p.transform, inf.transform)}"))
                        + $"; patch colliders {string.Join(",", col.Select(c => $"{c.GetType().Name} on {c.enabled} trigger {c.isTrigger} layer {c.gameObject.layer}"))}; player at {StateDump.Flat(p.transform.position - inf.transform.position):0}";
                yield return new WaitForSeconds(0.6f);
            }
            yield return new WaitForSeconds(1f);
            yield return Walk(Centre, 60f); // off the patch, which hurts while we stand in it
            var refused = LogLines().Skip(mark).FirstOrDefault(l => l.Contains("refused") && l.Contains("infection"));
            r.Info = $"{type} (sensor flaming {flaming}) at {rf} {infAt}: cleared {inf == null || inf.disappearing}{seen}; stood at {string.Join(" ", tried)}";
            if (inf != null && !inf.disappearing) r.Fail(r.Info);
            else if (refused != null) r.Fail($"{r.Info}; {refused}");
        }

        /// <summary>"infection-check station[,infection:client] [--from-other]": those patches are gone here (and the log says the other game's clear arrived).</summary>
        private static IEnumerator InfectionCheck(string[] a, Result r)
        {
            var which = (a.Length > 0 ? a[0] : "station").Split(',');
            var missing = new List<string>();
            if (a.Contains("--from-other")) yield return WaitLog(0, new[] { @"\[world\] infection cleared: [1-9]" }, 6f, missing);
            else yield return new WaitForSeconds(1f);
            var parts = new List<string>();
            foreach (var w in which)
            {
                if (!_noted.TryGetValue(w, out var at)) { r.Fail($"{w}'s patch wasn't noted (infection-note first)"); continue; }
                var near = Resources.FindObjectsOfTypeAll<Infection>().Where(i => i != null && i.gameObject.scene.IsValid() && !i.disappearing && StateDump.Flat(i.transform.position - at) <= 20f).ToList();
                parts.Add($"{w}: {near.Count} left at {Fmt(at)}");
                if (near.Count > 0) r.Fail($"{w}'s infection is still here ({string.Join(", ", near.Select(i => ObjRef.Of(i.gameObject)))})");
            }
            var line = LogLines().LastOrDefault(l => l.Contains("[world] infection cleared"));
            parts.Add(line != null ? line.Substring(line.IndexOf("[world]", StringComparison.Ordinal)) : "no 'infection cleared' line");
            r.Info = string.Join("; ", parts);
            if (missing.Count > 0) r.Fail($"not in the log: {missing[0]}; {r.Info}");
            else if (r.Failure != null) r.Failure += "; " + r.Info;
        }
    }
}
