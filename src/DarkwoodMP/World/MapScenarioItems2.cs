using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// Steps of the two-game map test for items used through the game's own code: firearms fired and reloaded at a
    /// spawned dog (Player.attack, fireWeapon, reload), things thrown (Player.attack, throwItem), a lantern worn on
    /// the hotbar (its light around the other player), and map items read (the places on the others' maps). The
    /// order they run in is tools/tests/map_scenario_items2.py.
    ///
    /// The dogs are spawned by the host at fixed offsets from the centre (<see cref="Spots"/>) and found in the
    /// client's game as the host's enemy nearest that spot. Waits are in real time.
    /// </summary>
    public static class MapScenarioItems2
    {
        private static readonly Dictionary<string, Vector3> Spots = new Dictionary<string, Vector3>
        {
            ["meat"] = new Vector3(500f, 0f, 1300f),   // the dog things are thrown at
            ["gun"] = new Vector3(-500f, 0f, 1300f),   // the dog that's shot
            ["fire"] = new Vector3(0f, 0f, -1300f),    // open ground for a molotov
            ["dark"] = new Vector3(-1300f, 0f, -600f), // away from the stations' lamps, for the lantern
        };
        private static Vector3 Spot(string which) => Spots.TryGetValue(which, out var off) ? Centre + off : Centre;

        private static Player P => Player.Instance;
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private static string ItemName(InvItemClass i) => InvItemClass.isNull(i) ? "nothing" : i.type;

        private static readonly System.Reflection.MethodInfo AttackM = AccessTools.Method(typeof(Player), "attack", new[] { typeof(float) });
        private static readonly System.Reflection.MethodInfo ReloadM = AccessTools.Method(typeof(Player), "reload", Type.EmptyTypes);
        private static readonly AccessTools.FieldRef<Player, GameObject> HeldItem = AccessTools.FieldRefAccess<Player, GameObject>("heldItem");
        private static readonly AccessTools.FieldRef<Explodes, bool> ExplodesActivated = AccessTools.FieldRefAccess<Explodes, bool>("activated");

        /// <summary>Where the mouse points, as Core.mouseWorldPosition says (while a step aims at something; null: the real mouse).</summary>
        internal static Vector3? AimAt;
        internal static bool Recording;

        /// <summary>The sounds the host's characters heard lately (Character.heardSound): who, where, dangerous, when.</summary>
        internal static readonly List<(Character c, Vector3 pos, bool dangerous, float at)> Heard = new List<(Character, Vector3, bool, float)>();

        /// <summary>
        /// "i2-safe on|off": our player is healed whenever it's below half health
        /// (MapScenarioSafePatch), so it can't die. Shot dogs bite back, a gas cloud drifts and a molotov's fire
        /// spreads: a test player dying changes the story flags the dumps compare, and nothing these steps check is
        /// about our player's health. (It isn't made invulnerable: a dog whose bites don't land keeps chasing its
        /// target and never goes for a lure.)
        /// </summary>
        internal static bool Safe;

        private static IEnumerator SafeStep(string[] a, Result r)
        {
            Safe = a.Length == 0 || a[0] != "off";
            if (Safe) MapScenarioSafePatch.Postfix(P);
            r.Info = $"safe {Safe}, health {P.health:0}";
            yield break;
        }

        public static void Register()
        {
            MapScenario.Add("i2-safe", SafeStep);
            Recording = true;
            MapScenario.Add("i2-dog", Dog);
            MapScenario.Add("i2-note", Note);
            MapScenario.Add("i2-fire", Fire, timeout: 90f);
            MapScenario.Add("i2-hurt", Hurt, timeout: 15f);
            MapScenario.Add("i2-shots-seen", ShotsSeen, timeout: 15f);
            MapScenario.Add("i2-throw", Throw, timeout: 40f);
            MapScenario.Add("i2-seen", Seen, timeout: 30f);
            MapScenario.Add("i2-heard", HeardStep, timeout: 15f);
            MapScenario.Add("i2-eats", Eats, timeout: 70f);
            MapScenario.Add("i2-gone", Gone, timeout: 30f);
            MapScenario.Add("i2-gassed", Gassed, timeout: 15f);
            MapScenario.Add("i2-kill", Kill, timeout: 20f);
            MapScenario.Add("i2-goto", (a, r) => Walk(FreeSpot(Spot(a.Length > 0 ? a[0] : "")), 40f), timeout: 40f);
            MapScenario.Add("i2-lantern", Lantern, timeout: 15f);
            MapScenario.Add("i2-lit", Lit, timeout: 15f);
            MapScenario.Add("i2-map-pick", MapPick);
            MapScenario.Add("i2-map-use", MapUse);
            MapScenario.Add("i2-map-check", MapCheck, timeout: 20f);
        }

        // ---- helpers ----------------------------------------------------------------------------------------------

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

        private static readonly Dictionary<string, Character> _dogs = new Dictionary<string, Character>();   // host: by spot
        private static readonly Dictionary<string, uint> _dogIds = new Dictionary<string, uint>();            // client: by spot

        /// <summary>The dog of that spot: the host's own, or the client's copy of it.</summary>
        private static Character DogOf(string name)
        {
            var s = Net.Session.Instance;
            var ne = NetEnemies.Instance;
            if (s == null || !s.IsClient) return _dogs.TryGetValue(name, out var d) && d != null ? d : null;
            if (ne == null) return null;
            if (_dogIds.TryGetValue(name, out uint id))
            {
                var had = ne.PuppetOf(id);
                if (had != null && had.alive) return had;
                _dogIds.Remove(name); // gone or dead: the host may have spawned another for this spot
            }
            var c = ne.PuppetCharacters().Where(x => x != null && x.name.StartsWith("Dog"))
                .OrderBy(x => StateDump.Flat(x.transform.position - Spot(name))).FirstOrDefault();
            if (c != null && StateDump.Flat(c.transform.position - Spot(name)) < 600f && ne.NetIdOf(c, out id)) { _dogIds[name] = id; return c; }
            return null;
        }

        private static IEnumerator WaitDog(string name, Action<Character> got)
        {
            float until = Time.realtimeSinceStartup + 6f;
            Character c = null;
            while ((c = DogOf(name)) == null && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.3f);
            got(c);
        }

        /// <summary>The shot dog bites back: our player is healed before it can kill us (a death changes the story
        /// flags the dumps compare).</summary>
        private static void StayAlive(Player p)
        {
            if (p.alive && p.health < p.maxHealth * 0.5f) p.setHealth(p.maxHealth);
        }

        private static void Face(Vector3 at)
        {
            var d = at - P.transform.position; d.y = 0f;
            if (d.sqrMagnitude > 1f) P.transform.rotation = Quaternion.Euler(90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
        }

        /// <summary>A spot `dist` from `target` on the side of the centre (where we come from).</summary>
        private static Vector3 Stand(Vector3 target, float dist)
        {
            var d = Centre - target; d.y = 0f;
            if (d.sqrMagnitude < 1f) d = Vector3.back;
            d.Normalize();
            // The host to one side and the client to the other: standing on one spot, each one's throws and shots
            // hit the other player in the way.
            var s = Net.Session.Instance;
            var side = new Vector3(-d.z, 0f, d.x) * (s != null && s.IsClient ? -1f : 1f);
            return FreeSpot(target + (d + side).normalized * dist);
        }

        private static (InvSlot slot, InvItemClass was) _hotbarWas, _bagWas;

        /// <summary>TYPE in a hotbar slot (a free one, else the last, its item put back by Restore), selected and in our hands.</summary>
        private static IEnumerator TakeOut(string type, Result r, Action<InvItemClass> made = null)
        {
            var p = P;
            // A world bear trap on the way here holds our player and no item comes out until it's freed (as
            // HealNow frees it).
            if (p.inBearTrap) { p.stopAllPerformingActionAnims(); yield return new WaitForSeconds(0.3f); }
            int free = p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
            if (free < 0) free = p.Hotbar.slots.Count - 1;
            var slot = p.Hotbar.slots[free];
            slot.inventory = p.Hotbar;
            if (!InvItemClass.isNull(slot.invItem)) { _hotbarWas = (slot, new InvItemClass(slot.invItem)); slot.removeItem(); }
            slot.createItem(type, 1);
            made?.Invoke(slot.invItem);
            foreach (var s in p.Hotbar.slots) s.deselect();
            slot.select(noiseless: true);
            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem) || p.currentItem.type != type || p.switchingItem) && Time.realtimeSinceStartup < until) yield return null;
            if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != type) r.Fail($"couldn't take the {type} out (holding {ItemName(p.currentItem)})");
        }

        /// <summary>N of TYPE in our bags (in a free slot, else the last, its item put back by Restore).</summary>
        private static void GiveBag(string type, int n)
        {
            var p = P;
            var slot = p.Inventory.slots.FirstOrDefault(s => InvItemClass.isNull(s.invItem)) ?? p.Inventory.slots.Last();
            slot.inventory = p.Inventory;
            if (!InvItemClass.isNull(slot.invItem)) { _bagWas = (slot, new InvItemClass(slot.invItem)); slot.removeItem(); }
            slot.createItem(type, n);
            _bagSlot = slot;
        }
        private static InvSlot _bagSlot;

        /// <summary>The hotbar and bag slots TakeOut and GiveBag used, as they were.</summary>
        private static void Restore()
        {
            foreach (var (slot, was) in new[] { _hotbarWas, _bagWas })
            {
                if (slot == null) continue;
                if (!InvItemClass.isNull(slot.invItem)) slot.removeItem();
                if (was != null) slot.createItem(was);
            }
            _hotbarWas = _bagWas = (null, null);
        }

        private static List<string> LogSince(int mark, string pattern) =>
            MapScenarioIssues.LogLines().Skip(mark).Where(l => Regex.IsMatch(l, pattern)).ToList();

        // ---- the dogs ---------------------------------------------------------------------------------------------

        private static readonly Dictionary<string, float> _noted = new Dictionary<string, float>();
        private static int _mark, _shotsNoted;
        private static HashSet<GameObject> _spawnedAtNote = new HashSet<GameObject>();
        private static HashSet<Burn> _burnsAtNote = new HashSet<Burn>();

        /// <summary>
        /// "i2-dog NAME [HEALTH] [--hungry]" (host): a Dog at NAME's spot, not set on anyone (with that much health).
        /// --hungry makes it very hungry (Character.veryHungry): it goes for food even while it's chasing someone.
        /// A dog that has seen the thrower (the noise lure brings it within sight of them) chases them, and a chasing
        /// dog that isn't very hungry passes over a lure (Character.checkForLure).
        /// </summary>
        private static IEnumerator Dog(string[] a, Result r)
        {
            bool hungry = System.Array.IndexOf(a, "--hungry") >= 0;
            a = System.Array.FindAll(a, s => s != "--hungry");
            string name = a.Length > 0 ? a[0] : "gun";
            var spot = FreeSpot(Spot(name));
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, "Dog", nocturnal: false, attackPlayer: false);
            if (c == null) { r.Fail("couldn't spawn a dog"); yield break; }
            c.transform.position = spot;
            _dogs[name] = c;
            yield return new WaitForSeconds(1.5f); // it's set up (its health is 0 until then)
            if (a.Length > 1) { c.maxHealth = F(a[1]); c.health = F(a[1]); }
            if (hungry) c.veryHungry = true;
            _noted[name] = c.health;
            NetEnemies.Instance.NetIdOf(c, out uint id);
            r.Info = $"{c.name} #{id} at {Fmt(c.transform.position)}, health {c.health}, eats {c.lureType}{(c.veryHungry ? ", very hungry" : "")}";
        }

        /// <summary>"i2-note [NAME]": note the dog's health here, the log's length and the other players' shots seen.</summary>
        private static IEnumerator Note(string[] a, Result r)
        {
            _mark = MapScenarioIssues.LogMark();
            _shotsNoted = Sync.PlayerFx.ShotsSeen;
            Heard.Clear();
            _spawnedAtNote = new HashSet<GameObject>(ThrownHits.Spawned.Where(g => g != null));
            _burnsAtNote = new HashSet<Burn>(Object.FindObjectsOfType<Burn>());
            if (a.Length > 0) { var d = DogOf(a[0]); if (d != null) _noted[a[0]] = d.health; }
            r.Info = $"log line {_mark}, shots seen {_shotsNoted}" + (a.Length > 0 && _noted.TryGetValue(a[0], out float h) ? $", {a[0]} dog health {h:0}" : "");
            yield break;
        }

        /// <summary>"i2-hurt NAME" (host): the dog lost health since i2-note, and no hit on it was refused.</summary>
        private static IEnumerator Hurt(string[] a, Result r)
        {
            string name = a.Length > 0 ? a[0] : "gun";
            yield return new WaitForSeconds(1f);
            var d = DogOf(name);
            if (d == null) { r.Fail($"no {name} dog"); yield break; }
            float was = _noted.TryGetValue(name, out float h) ? h : d.maxHealth;
            var refused = LogSince(_mark, @"refused EnemyHit");
            var capped = LogSince(_mark, @"\[authority\] .*'s hit on .* is more than");
            r.Info = $"{d.name}: health {was:0} -> {d.health:0}, alive {d.alive}; {refused.Count} hit(s) refused, {capped.Count} capped";
            if (!(d.health < was) && d.alive) r.Fail("no damage here: " + r.Info);
            if (refused.Count > 0) r.Fail(r.Info + ": " + refused[0].Trim());
            _noted[name] = d.health;
        }

        /// <summary>"i2-shots-seen MIN": at least MIN of the other player's shots were shown on their puppet since i2-note.</summary>
        private static IEnumerator ShotsSeen(string[] a, Result r)
        {
            int want = a.Length > 0 ? int.Parse(a[0]) : 1;
            float until = Time.realtimeSinceStartup + 5f;
            while (Sync.PlayerFx.ShotsSeen - _shotsNoted < want && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.3f);
            int n = Sync.PlayerFx.ShotsSeen - _shotsNoted;
            r.Info = $"{n} shot(s) shown on the other player's puppet";
            if (n < want) r.Fail(r.Info + $", not {want}");
        }

        /// <summary>"i2-kill NAME" (host): the dog killed (as by our player), so it doesn't follow anyone around later steps.</summary>
        private static IEnumerator Kill(string[] a, Result r)
        {
            var d = DogOf(a.Length > 0 ? a[0] : "gun");
            if (d == null) { r.Info = "no dog (gone already)"; yield break; }
            if (d.alive) AsPlayer(() => d.getHit(d.health + 500f, P.transform, false, true, false));
            yield return new WaitForSeconds(1.5f);
            r.Info = $"{d.name} alive {d.alive}";
            if (d.alive) r.Fail("it's still alive");
        }

        // ---- firearms ---------------------------------------------------------------------------------------------

        /// <summary>
        /// "i2-fire TYPE NAME SHOTS": take the gun TYPE out empty, put its ammunition in our bags and reload it
        /// (Player.reload: the game's reload animation loads it), walk near the NAME dog and fire SHOTS shots at it
        /// (Player.attack), one per the gun's fire rate, reloading when it's empty. The host must accept every hit
        /// (no "the host refused hit on enemy" here). With --loaded the gun starts with a full clip and the first
        /// reload is skipped: the dog bites every second or so, each bite interrupts what the player is doing
        /// (Player.interruptAllActions), and a gun with a long reload animation (the burst rifle) never finishes one.
        /// </summary>
        private static IEnumerator Fire(string[] a, Result r)
        {
            var p = P;
            bool loaded = System.Array.IndexOf(a, "--loaded") >= 0;
            a = System.Array.FindAll(a, s => s != "--loaded");
            string type = a[0], name = a.Length > 1 ? a[1] : "gun";
            int shots = a.Length > 2 ? int.Parse(a[2]) : 3;
            Character dog = null;
            yield return WaitDog(name, c => dog = c);
            if (dog == null) { r.Fail($"no {name} dog here"); yield break; }
            int mark = MapScenarioIssues.LogMark();
            try
            {
                yield return TakeOut(type, r, it => it.ammo = loaded ? it.baseClass.clipSize : 0);
                if (r.Failure != null) yield break;
                var gun = p.currentItem;
                var b = gun.baseClass;
                if (!b.isFirearm) { r.Fail($"a {type} isn't a firearm"); yield break; }
                GiveBag(b.ammoType, b.clipSize * 2 + 4);
                string loads = "";
                IEnumerator Reload()
                {
                    int before = gun.ammo;
                    float until = Time.realtimeSinceStartup + 15f;
                    // The dog bites while we reload, and a bite (Player.gettingHit) stops a reload starting: ask
                    // again until it starts, as a player would press the key again.
                    while (!p.reloading && gun.ammo == before && Time.realtimeSinceStartup < until)
                    {
                        AsPlayer(() => ReloadM.Invoke(p, null));
                        yield return new WaitForSeconds(0.3f);
                        StayAlive(p);
                    }
                    while ((p.reloading || gun.ammo == before) && Time.realtimeSinceStartup < until) { StayAlive(p); yield return null; }
                    loads += (loads == "" ? "" : ", ") + $"{before}->{gun.ammo}";
                    if (gun.ammo <= before) r.Fail($"the {type} didn't reload ({before} -> {gun.ammo} of {b.clipSize}, reloading {p.reloading}, {b.ammoType} in bags {p.Inventory.getItemInPlayer(b.ammoType)?.amount ?? 0})");
                }
                if (loaded) loads = $"loaded {gun.ammo}";
                else yield return Reload();
                if (r.Failure != null) yield break;
                int fired = 0;
                var aimed = new List<string>();
                int moved = 0;
                for (int i = 0; i < shots && dog != null; i++)
                {
                    if (gun.ammo <= 0) { yield return Reload(); if (r.Failure != null) yield break; }
                    var at = dog.transform.position;
                    if (i == 0 || StateDump.Flat(at - p.transform.position) > 260f) yield return Walk(Stand(at, 180f), 30f);
                    at = dog.transform.position;
                    AimAt = at;
                    Face(at);
                    // A wall between us and the dog takes every shot (run on 3caf891: the host's six pistol shots all
                    // hit Wall_upDown 105 away, the dog 180 away): step round the dog until the line of fire is clear,
                    // as a player would.
                    string first = LineOfFire(p, dog, out bool clear);
                    var round = p.transform.position - at; round.y = 0f;
                    for (int k = 1; k < 6 && !clear && dog != null; k++)
                    {
                        moved++;
                        yield return Walk(FreeSpot(dog.transform.position + Quaternion.Euler(0f, 60f * k, 0f) * round.normalized * 180f), 15f);
                        at = dog.transform.position;
                        AimAt = at;
                        Face(at);
                        first = LineOfFire(p, dog, out clear);
                    }
                    aimed.Add(first);
                    int ammo = gun.ammo;
                    p.currentAim = b.minAim; // as steady as aiming makes it (Player's aim narrows it to minAim)
                    AsPlayer(() => AttackM.Invoke(p, new object[] { 1f }));
                    if (gun.ammo < ammo) fired++;
                    StayAlive(p);
                    yield return new WaitForSeconds(Mathf.Max(b.fireRate, 0.05f) + 0.05f);
                }
                yield return new WaitForSeconds(1.5f); // the host's answers
                var sent = LogSince(mark, @"\[authority\] request \d+: hit on enemy");
                var refused = LogSince(mark, @"the host refused hit on enemy");
                r.Info = $"{type} ({b.fireMode}, {b.projectileAmount} pellet(s), every {b.fireRate:0.00} s, clip {b.clipSize}, {b.ammoType}): "
                       + $"reloads {loads}; {fired} of {shots} fired, {sent.Count} hit(s) sent to the host, {refused.Count} refused; {(dog != null ? $"{dog.name} health here {dog.health:0}" : "the dog is gone")}; first in the line of fire "
                       + string.Join(", ", aimed.Distinct().Select(x => $"{x} x{aimed.Count(y => y == x)}")) + (moved > 0 ? $" ({moved} step(s) round the dog for a clear shot)" : "");
                if (fired < shots) r.Fail("not every shot fired: " + r.Info);
                else if (refused.Count > 0) r.Fail(r.Info + ": " + refused[0].Trim());
                else if (Net.Session.Instance != null && Net.Session.Instance.IsClient && sent.Count == 0) r.Fail("no hit reached the host's dog: " + r.Info);
            }
            finally
            {
                AimAt = null;
                Restore();
            }
            // (We stay there: a dog that's been shot follows the shooter, and back at the centre it ran into the
            // stations' traps.)
        }

        /// <summary>What the shot's ray (Player.spawnBullet: from our player along its facing) meets first, as
        /// "NAME@DISTANCE"; `clear` when that's the dog.</summary>
        private static string LineOfFire(Player p, Character dog, out bool clear)
        {
            clear = false;
            if (!Physics.Raycast(p.transform.position + p.transform.up, p.transform.up, out var hit, 1000f, 18909185) || hit.collider == null) return "nothing";
            var t = hit.collider.transform;
            clear = dog != null && (t.GetComponent<Character>() == dog || (t.parent != null && t.parent.GetComponent<Character>() == dog));
            return $"{(t.parent != null ? t.parent.name : t.name)}@{hit.distance:0}";
        }

        // ---- throwing ---------------------------------------------------------------------------------------------

        private static GameObject _thrown;

        /// <summary>
        /// "i2-throw TYPE AT": throw TYPE at AT (a dog's name, or a spot) the way the game does: take it out, aim (the
        /// held item, as Player's aiming makes it for a throwable) and attack (Player.attack calls Player.throwItem),
        /// with the mouse pointing at AT. Waits for it to land.
        /// </summary>
        private static IEnumerator Throw(string[] a, Result r)
        {
            var p = P;
            string type = a[0], at = a.Length > 1 ? a[1] : "fire";
            Character dog = null;
            if (at == "meat" || at == "gun") { yield return WaitDog(at, c => dog = c); if (dog == null) { r.Fail($"no {at} dog here"); yield break; } }
            var target = dog != null ? dog.transform.position : FreeSpot(Spot(at));
            yield return Walk(Stand(target, type == "gasBomb" ? 340f : 250f), 30f); // (out of a gas cloud's reach)
            if (dog != null) target = dog.transform.position;
            try
            {
                yield return TakeOut(type, r);
                if (r.Failure != null) yield break;
                if (!p.currentItem.baseClass.isThrowable) { r.Fail($"a {type} isn't throwable"); yield break; }
                // As Player's aim does for a throwable (the held item, hidden, not hitting us). A bite as we aim
                // puts the held item away (Player.hideHeldItem destroys it) and nothing is thrown: aim again.
                GameObject held = null;
                bool threw = false;
                for (int attempt = 0; attempt < 4 && !threw; attempt++)
                {
                    StayAlive(p);
                    if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != type)
                    {
                        yield return TakeOut(type, r);
                        if (r.Failure != null) yield break;
                    }
                    held = Core.AddPrefab(p.currentItem.baseClass.item, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), p.gameObject);
                    if (held == null) { r.Fail($"couldn't make a held {type}"); yield break; }
                    var ren = held.GetComponent<Renderer>(); if (ren != null) ren.enabled = false;
                    foreach (var hc in held.GetComponentsInChildren<Collider>())
                        foreach (var pc in p.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(hc, pc);
                    HeldItem(p) = held;
                    AimAt = target;
                    Face(target);
                    yield return new WaitForSeconds(0.3f);
                    if (dog != null) { target = dog.transform.position; AimAt = target; }
                    Face(target);
                    if (held == null || HeldItem(p) != held) { yield return new WaitForSeconds(0.5f); continue; } // put away by a bite
                    int had = p.currentItem.amount;
                    AsPlayer(() => AttackM.Invoke(p, new object[] { 1f }));
                    if (HeldItem(p) != null) { r.Fail($"the {type} wasn't thrown (still held)"); yield break; }
                    threw = InvItemClass.isNull(p.currentItem) || p.currentItem.amount < had;
                    if (!threw) { if (held != null) Object.Destroy(held); yield return new WaitForSeconds(0.5f); }
                }
                if (!threw) { r.Fail($"the {type} was never thrown (put away by bites each time)"); yield break; }
                _thrown = held;
                // A gas bomb thrown at a dog right beside us can break on it at once: gone before we look.
                var ti = held != null ? held.GetComponent<ThrownItem>() : null;
                float until = Time.realtimeSinceStartup + 6f;
                while (held != null && ti != null && !ti.onGround && Time.realtimeSinceStartup < until) { StayAlive(p); yield return null; }
                r.Info = $"{type} thrown at {at} {Fmt(target)} from {Fmt(p.transform.position)}: "
                       + (held == null ? "gone on landing" : $"at {Fmt(held.transform.position)}, on the ground {ti != null && ti.onGround}");
            }
            finally { AimAt = null; Restore(); }
            yield return new WaitForSeconds(1f);
        }

        private static readonly HashSet<GameObject> _seenBefore = new HashSet<GameObject>();

        /// <summary>
        /// "i2-seen TYPE AT [lands|fire|off]": the other player's TYPE came here (ThrownHits.Copies) and landed near AT;
        /// "fire": something caught fire there or it left fire behind; "off": it went off (Explodes) or is gone.
        /// </summary>
        private static IEnumerator Seen(string[] a, Result r)
        {
            string type = a[0], at = a.Length > 1 ? a[1] : "fire", want = a.Length > 2 ? a[2] : "lands";
            var it = Singleton<ItemsDatabase>.Instance.getItem(type, instantiate: false);
            if (it == null || it.item == null) { r.Fail($"no {type} in the database"); yield break; }
            string prefab = it.item.name;
            var dog = at == "meat" || at == "gun" ? DogOf(at) : null;
            var near = dog != null ? dog.transform.position : FreeSpot(Spot(at));
            GameObject copy = null;
            float until = Time.realtimeSinceStartup + 8f;
            while (copy == null && Time.realtimeSinceStartup < until)
            {
                copy = ThrownHits.Copies.FirstOrDefault(g => g != null && !_seenBefore.Contains(g) && g.name.StartsWith(prefab, StringComparison.Ordinal));
                if (copy == null) yield return new WaitForSeconds(0.2f);
            }
            bool gone = false;
            if (copy == null)
            {
                // It may have been and gone already (a gas bomb or a molotov is destroyed as it breaks): the log says
                // it came; what it left is looked for where it was thrown.
                var came = LogSince(_mark, $@"\[world\] .* threw {Regex.Escape(type)}\s*$");
                if (came.Count == 0) { r.Fail($"no copy of the other player's {type} here (none since i2-note)"); yield break; }
                gone = true;
            }
            else _seenBefore.Add(copy);
            var ti = copy != null ? copy.GetComponent<ThrownItem>() : null;
            var ex = copy != null ? copy.GetComponent<Explodes>() : null;
            Vector3 last = copy != null ? copy.transform.position : near;
            bool landed = gone, off = gone, fire = false;
            until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                if (copy != null) { last = copy.transform.position; landed |= ti != null && ti.onGround; off |= ex != null && ExplodesActivated(ex); }
                else { landed = off = true; }
                // (new since i2-note: what earlier throws left there doesn't count)
                fire = ThrownHits.Spawned.Any(g => g != null && !_spawnedAtNote.Contains(g) && StateDump.Flat(g.transform.position - last) < 400f)
                    || Object.FindObjectsOfType<Burn>().Any(b => b != null && !_burnsAtNote.Contains(b) && b.GetComponent<Player>() == null && StateDump.Flat(b.transform.position - last) < 400f);
                if (want == "lands" ? landed : want == "off" ? off : fire) break;
                yield return new WaitForSeconds(0.2f);
            }
            float d = StateDump.Flat(last - near);
            r.Info = $"{(copy != null ? copy.name : prefab + (gone ? " (gone already)" : ""))} at {Fmt(last)} ({d:0} from {at}): landed {landed}, went off {off}, fire {fire}";
            bool ok = want == "lands" ? landed : want == "off" ? off : fire;
            if (!ok) r.Fail($"it didn't {(want == "lands" ? "land" : want == "off" ? "go off" : "leave fire")}: {r.Info}");
            else if (d > 500f) r.Fail($"it landed far from where it was thrown: {r.Info}");
            if (want == "fire" && r.Failure == null)
            {
                // A molotov's gasoline is the host's (Containers.ThrownSpawns): here it's the one the host shared,
                // and every fire the host lit in it is found.
                yield return new WaitForSeconds(1f);
                var fuel = Object.FindObjectsOfType<Liquid>().Where(l => l != null && l.name.StartsWith("Gasoline") && StateDump.Flat(l.transform.position - last) < 400f).ToList();
                int shared = fuel.Count(l => ObjRef.Of(l.gameObject).K == ObjRef.Kind.Spawned);
                var lost = LogSince(_mark, @"got LiquidLit .*not found here");
                r.Info += $"; gasoline here {fuel.Count} ({shared} shared, {fuel.Count(l => l.burning)} burning), {lost.Count} fire(s) not found";
                if (fuel.Count == 0 || shared < fuel.Count) r.Fail("the molotov's gasoline isn't the shared one: " + r.Info);
                else if (lost.Count > 0) r.Fail(r.Info + ": " + lost[0].Trim());
            }
        }

        /// <summary>"i2-heard NAME" (host): the NAME dog heard a sound (not a dangerous one) from the thrown thing since i2-note.</summary>
        private static IEnumerator HeardStep(string[] a, Result r)
        {
            var d = DogOf(a.Length > 0 ? a[0] : "meat");
            if (d == null) { r.Fail("no dog"); yield break; }
            float until = Time.realtimeSinceStartup + 6f;
            while (!Heard.Any(h => h.c == d && !h.dangerous) && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.2f);
            var got = Heard.Where(h => h.c == d).ToList();
            r.Info = $"{d.name} heard {got.Count} sound(s): " + string.Join(", ", got.Take(4).Select(h => $"{Fmt(h.pos)}{(h.dangerous ? " (dangerous)" : "")}"))
                   + $"; behaviour {d.behaviour}";
            if (!got.Any(h => !h.dangerous)) r.Fail("it heard nothing: " + r.Info);
        }

        /// <summary>"i2-eats NAME" (host): the NAME dog goes for the thrown lure and eats it up.</summary>
        private static IEnumerator Eats(string[] a, Result r)
        {
            var d = DogOf(a.Length > 0 ? a[0] : "meat");
            if (d == null) { r.Fail("no dog"); yield break; }
            var lure = Object.FindObjectsOfType<Lure>().Where(l => l != null).OrderBy(l => StateDump.Flat(l.transform.position - d.transform.position)).FirstOrDefault();
            if (lure == null) { r.Fail("no lure here"); yield break; }
            string lname = lure.name;
            bool went = false, ate = false;
            float until = Time.realtimeSinceStartup + 30f;
            while (lure != null && !ate && Time.realtimeSinceStartup < until)
            {
                went |= d.headingForLure; ate |= d.eating;
                yield return new WaitForSeconds(0.2f);
            }
            // A piece of meat lasts the game a long while (its health is in the thousands, one bite a frame of the
            // eating loop): once the dog is eating, leave it one bite, which the dog's own next bite takes.
            if (lure != null && ate && lure.health > 1) lure.health = 1;
            until = Time.realtimeSinceStartup + 20f;
            while (lure != null && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.2f);
            r.Info = $"{d.name} went for {lname}: {went}, ate: {ate}, the lure is {(lure == null ? "eaten up" : $"still there (health {lure.health})")}";
            if (lure != null) r.Fail(r.Info);
        }

        /// <summary>"i2-gone TYPE": our thrown TYPE (the last i2-throw) is gone from this game (eaten in the host's).</summary>
        private static IEnumerator Gone(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (_thrown != null && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.2f);
            r.Info = _thrown == null ? "gone" : $"{_thrown.name} still at {Fmt(_thrown.transform.position)}";
            if (_thrown != null) r.Fail(r.Info);
        }

        /// <summary>"i2-gassed NAME" (host): the NAME dog was caught in the gas (the effect on it, or health lost since i2-note).</summary>
        private static IEnumerator Gassed(string[] a, Result r)
        {
            string name = a.Length > 0 ? a[0] : "meat";
            var d = DogOf(name);
            if (d == null) { r.Fail("no dog"); yield break; }
            float was = _noted.TryGetValue(name, out float h) ? h : d.maxHealth;
            bool gas = false;
            float until = Time.realtimeSinceStartup + 6f;
            while (!(gas = d.effects != null && d.effects.hasEffectType(CharacterEffectType.gas)) && d.health >= was && Time.realtimeSinceStartup < until)
                yield return new WaitForSeconds(0.2f);
            r.Info = $"{d.name}: gas effect {gas}, health {was:0} -> {d.health:0}";
            if (!gas && d.health >= was) r.Fail(r.Info);
        }

        // ---- the lantern ------------------------------------------------------------------------------------------

        private static (InvSlot slot, InvItemClass was) _lanternWas;

        /// <summary>"i2-lantern on|off":a lantern put on our hotbar (lit, as the game does for one there) or taken off.</summary>
        private static IEnumerator Lantern(string[] a, Result r)
        {
            var p = P;
            bool on = a.Length == 0 || a[0] == "on";
            var ctl = Singleton<Controller>.Instance;
            var slot = p.Hotbar.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == "lantern");
            if (on && slot == null)
            {
                // A free slot, else the last one not in hand (its item put back when the lantern comes off).
                slot = p.Hotbar.slots.LastOrDefault(s => InvItemClass.isNull(s.invItem) && !s.selected) ?? p.Hotbar.slots.LastOrDefault(s => !s.selected);
                if (slot == null) { r.Fail("no hotbar slot"); yield break; }
                slot.inventory = p.Hotbar;
                if (!InvItemClass.isNull(slot.invItem)) { _lanternWas = (slot, new InvItemClass(slot.invItem)); slot.removeItem(); }
                AsPlayer(() => { slot.createItem("lantern", 1); slot.invItem.checkForActiveSwitches(); });
            }
            else if (!on && slot != null)
            {
                AsPlayer(() => { slot.invItem.deactivateSwitches("lantern"); slot.removeItem(); });
                if (_lanternWas.slot == slot && _lanternWas.was != null) slot.createItem(_lanternWas.was);
                _lanternWas = (null, null);
            }
            yield return new WaitForSeconds(0.5f);
            bool lit = ctl.logicLights.Contains(p.lightDot) && p.lightDot.lightsPlayer;
            r.Info = $"lantern {(on ? "on" : "off")}: our light dot {(lit ? "lit" : "not lit")}, radius {p.lightDot.LightRadius:0}";
            if (lit != on) r.Fail(r.Info);
        }

        /// <summary>"i2-lit yes|no" (host): the other player's puppet shows a lantern light, and their stand-in stands in light (Core.isInLight).</summary>
        private static IEnumerator Lit(string[] a, Result r)
        {
            bool want = a.Length == 0 || a[0] == "yes";
            var ps = Sync.PlayerSync.Instance;
            Sync.RemotePlayer rp = null;
            NetPlayerProxy proxy = null;
            bool shown = false, inLight = false;
            float until = Time.realtimeSinceStartup + 6f;
            while (true)
            {
                rp = ps?.Remotes.Values.FirstOrDefault(x => x.Owner != Sync.PlayerSync.DummyId);
                if (rp != null) NetPlayerProxy.All.TryGetValue(rp.Owner, out proxy);
                shown = rp != null && rp.LanternShown > 0f;
                var pos = proxy != null ? proxy.transform.position : rp?.Last?.Position ?? Vector3.zero;
                inLight = rp != null && Core.isInLight(pos, mustBeWalkable: false);
                if ((shown == want && inLight == want) || Time.realtimeSinceStartup > until) break;
                yield return new WaitForSeconds(0.3f);
            }
            r.Info = $"{rp?.Name ?? "nobody"}: lantern light on their puppet {shown} (radius {rp?.LanternShown ?? 0f:0}), their {(proxy != null ? "stand-in" : "puppet")} in light {inLight}";
            if (shown != want || inLight != want) r.Fail(r.Info);
        }

        // ---- map items --------------------------------------------------------------------------------------------

        private static readonly string[] Maps = { "map_radioTower", "map_trainWreck", "map_villageTree", "map_pigShed", "map_mushrooms", "map_dziury", "map_borderBurned", "map_piotrekPigsheds", "map_doctorEntrance", "map", "map_bio2", "map_bio3", "map_doctor_ch2" };
        private static string _map;

        private static List<Location> PlacesOf(string type)
        {
            var it = Singleton<ItemsDatabase>.Instance.getItem(type, instantiate: false);
            var wg = Singleton<WorldGenerator>.Instance;
            if (it == null || !it.isMap || wg == null) return new List<Location>();
            return it.locations.Where(n => n != null).Select(n => wg.getLocation(n)).Where(l => l != null).ToList();
        }

        private static bool Shown(Location l)
        {
            var el = l.GetComponent<MapElement>();
            return Singleton<UI>.Instance.journal.locationsDict.ContainsKey(Core.getTrueLocationName(l.name)) && (el == null || el.isOnMap);
        }

        /// <summary>"i2-map-pick": the first map item none of whose places are in our journal yet (the same one in both games).</summary>
        private static IEnumerator MapPick(string[] a, Result r)
        {
            var j = Singleton<UI>.Instance.journal;
            _map = Maps.FirstOrDefault(m => { var ls = PlacesOf(m); return ls.Count > 0 && ls.All(l => !j.locationsDict.ContainsKey(Core.getTrueLocationName(l.name))); });
            if (_map == null) { r.Fail("every map's places are in the journal already"); yield break; }
            r.Info = $"{_map}: {string.Join(", ", PlacesOf(_map).Select(l => l.name))}";
        }

        /// <summary>"i2-map-use": read the picked map (InvItemClass.use: its places in the journal and on the map).</summary>
        private static IEnumerator MapUse(string[] a, Result r)
        {
            if (_map == null) { r.Fail("no map picked"); yield break; }
            // In our bags, as a map is read from there (use() takes it out of its slot).
            GiveBag(_map, 1);
            var item = _bagSlot.invItem;
            try { AsPlayer(() => item.use()); }
            finally { Restore(); }
            yield return new WaitForSeconds(0.5f);
            var ls = PlacesOf(_map);
            r.Info = $"{_map}: " + string.Join(", ", ls.Select(l => $"{l.name} {(Shown(l) ? "shown" : "hidden")}"));
            if (!ls.All(Shown)) r.Fail(r.Info);
        }

        /// <summary>"i2-map-check": the other player's picked map's places are in our journal and shown on our map.</summary>
        private static IEnumerator MapCheck(string[] a, Result r)
        {
            if (_map == null) { r.Fail("no map picked"); yield break; }
            var ls = PlacesOf(_map);
            float until = Time.realtimeSinceStartup + 12f; // the story state goes out every couple of seconds
            while (!ls.All(Shown) && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.5f);
            var j = Singleton<UI>.Instance.journal;
            r.Info = $"{_map}: " + string.Join(", ", ls.Select(l => $"{l.name} journal {j.locationsDict.ContainsKey(Core.getTrueLocationName(l.name))}, map {(l.GetComponent<MapElement>()?.isOnMap.ToString() ?? "no element")}"));
            if (!ls.All(Shown)) r.Fail(r.Info);
        }
    }

    /// <summary>The map test aims with the mouse at what a step points it at (Player.throwItem's distance, its facing).</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.mouseWorldPosition), new[] { typeof(bool) })]
    static class MapScenarioAimPatch
    {
        static void Postfix(ref Vector3 __result)
        {
            var at = MapScenarioItems2.AimAt;
            if (at.HasValue) __result = new Vector3(at.Value.x, __result.y, at.Value.z);
        }
    }

    /// <summary>The map test's i2-safe: our player healed whenever it's below half health (in Player.setHealth, before the game checks for a death).</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.setHealth))]
    static class MapScenarioSafePatch
    {
        internal static void Postfix(Player __instance)
        {
            if (!MapScenarioItems2.Safe || __instance == null || !__instance.alive) return;
            if (__instance.health < __instance.maxHealth * 0.5f) __instance.health = __instance.maxHealth;
        }
    }

    /// <summary>The map test: what the host's characters heard (Character.heardSound).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.heardSound), new[] { typeof(Vector3), typeof(float), typeof(bool), typeof(float), typeof(bool) })]
    static class MapScenarioHeardPatch
    {
        static void Postfix(Character __instance, Vector3 Position, bool dangerousSound)
        {
            if (!MapScenarioItems2.Recording) return;
            var h = MapScenarioItems2.Heard;
            if (h.Count > 200) h.RemoveAt(0);
            h.Add((__instance, Position, dangerousSound, Time.realtimeSinceStartup));
        }
    }
}
