using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps with several client games at once (tools/tests/map4_multi.py, run with tools/run-test.sh map4):
    /// an enemy's threat among several remote players, three players taking from one container slot at the same
    /// moment, two at one trader, things done while a third player is far away, and night attackers with players
    /// in different hideouts. Steps that happen "at the same moment" take an epoch second (the games share one
    /// machine's clock), which the driver fills in for a step run in several games at once ("c1+c2+c3").
    /// </summary>
    public static class MapScenarioMulti
    {
        private static Player P => Player.Instance;
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        public static void Register()
        {
            Add("mt-stand", Stand, timeout: 60f);
            Add("mt-home", HomeStep, timeout: 90f);
            Add("mt-tough", Tough);
            Add("mt-debug-threat", (a, r) => { Threat.Debug = a.Length == 0 || a[0] == "on"; r.Info = $"threat log {Threat.Debug}"; return Nothing(); });
            Add("mt-die", Die, timeout: 60f);
            Add("mt-spawn", SpawnEnemy, timeout: 15f);
            Add("mt-hit", HitEnemy, timeout: 40f);
            Add("mt-target", Target, timeout: 40f);
            Add("mt-never", Never);
            Add("mt-dead", Dead, timeout: 20f);
            Add("mt-corpse", Corpse, timeout: 30f);
            Add("mt-grab", Grab, timeout: 40f);
            Add("mt-talk", Talk, timeout: 30f);
            Add("mt-door-drawn", DoorDrawn, timeout: 15f);
            Add("mt-hideout-go", HideoutGo, timeout: 90f);
            Add("mt-night", Night, timeout: 40f);
            Add("mt-locks", Locks);
            Add("mt-leave", Leave, timeout: 15f);
            Add("mt-open-free", OpenFree, timeout: 20f);
        }

        /// <summary>"mt-stand DX DZ": walk to the centre plus (DX, DZ).</summary>
        private static IEnumerator Stand(string[] a, Result r)
        {
            var to = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            yield return Walk(to, 40f);
            r.Info = Fmt(P.transform.position);
        }

        /// <summary>"mt-home": the test area is the centre again (after far-spot), and we walk back to it.</summary>
        private static IEnumerator HomeStep(string[] a, Result r)
        {
            if (Home.HasValue) SetCentre(Home.Value);
            yield return Walk(Centre, 40f);
            r.Info = $"centre {Fmt(Centre)}, we're at {Fmt(P.transform.position)}";
        }

        private static IEnumerator Nothing() { yield break; }

        private static bool _tough;

        /// <summary>"mt-tough on|off": while on, our player is healed whenever below half health (an enemy biting
        /// them while the host's steps wait would kill them, and a death changes the story).</summary>
        private static IEnumerator Tough(string[] a, Result r)
        {
            bool on = a.Length == 0 || a[0] == "on";
            if (on && !_tough) Plugin.Instance.StartCoroutine(KeepAlive());
            _tough = on;
            r.Info = $"tough {on}";
            yield break;
        }

        private static IEnumerator KeepAlive()
        {
            while (true)
            {
                yield return null;
                if (!_tough) yield break;
                var p = P;
                if (p != null && p.alive && p.health < p.maxHealth * 0.5f) { p.bleeding = false; p.setHealth(p.maxHealth); }
            }
        }

        /// <summary>"mt-die": our player dies (Player.die) and respawns; the death's story flags (player_diedAtLeastOneTime)
        /// must reach the other games.</summary>
        private static IEnumerator Die(string[] a, Result r)
        {
            var p = P;
            if (p == null || !p.alive) { r.Fail("not alive"); yield break; }
            p.die();
            float until = Time.realtimeSinceStartup + 50f;
            yield return new WaitForSeconds(2f);
            while (Time.realtimeSinceStartup < until && !(P != null && P.alive && WorldShare.WorldReady)) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(FlagsSync.Interval + 2f);
            r.Info = $"alive again {P != null && P.alive} at {Fmt(P.transform.position)}";
            if (P == null || !P.alive) r.Fail(r.Info);
        }

        // ---- 1. threat among several remote players ------------------------------------------------------------

        private static Character _enemy;   // host: the one mt-spawn made
        private static uint _enemyId;      // its net ID
        // host: who it was after, in order, and for each time it was after someone, who else was nearer to it then
        private static readonly List<string> _targets = new List<string>();
        private static readonly Dictionary<string, HashSet<string>> _nearerWhile = new Dictionary<string, HashSet<string>>();

        private static string NameOf(Transform t)
        {
            if (t == null) return "-";
            if (P != null && t == P.transform) return "host";
            var px = t.GetComponent<NetPlayerProxy>();
            return px != null ? px.OwnerName : t.name;
        }

        /// <summary>The name a client game's player goes by on the host ("c2" is Client2, as tools/setup-client-game.sh names them).</summary>
        private static string PlayerName(string who) => who == "host" ? "host" : who.StartsWith("c") ? "Client" + who.Substring(1) : who;

        private static IEnumerator WatchEnemy(Character c)
        {
            string last = null;
            while (c != null && c == _enemy && c.alive)
            {
                string now = NameOf(c.target);
                if (now != last) { _targets.Add(now); last = now; Plugin.Log.LogInfo($"[map] {c.name} #{_enemyId} now after {now}"); }
                if (c.target != null && now != "-")
                {
                    float mine = StateDump.Flat(c.target.position - c.transform.position);
                    var players = NetPlayerProxy.All.Values.Where(x => x != null && x.alive).Select(x => (x.OwnerName, x.transform.position)).ToList();
                    if (P != null) players.Add(("host", P.transform.position));
                    foreach (var (name, pos) in players)
                        if (name != now && StateDump.Flat(pos - c.transform.position) + 30f < mine)
                        {
                            if (!_nearerWhile.TryGetValue(now, out var set)) _nearerWhile[now] = set = new HashSet<string>();
                            set.Add(name);
                        }
                }
                yield return null;
            }
        }

        private static string ThreatText(Character c) =>
            c == null ? "-" : string.Join(", ", Threat.Of(c).Values.Where(kv => kv.Key != null).Select(kv => $"{NameOf(kv.Key.transform)} {kv.Value:0.0}"));

        /// <summary>"mt-spawn KIND DX DZ" (host): an enemy at the centre plus (DX, DZ), not set on anyone; from now on
        /// who it goes after is recorded.</summary>
        private static IEnumerator SpawnEnemy(string[] a, Result r)
        {
            if (Net.Session.Instance != null && Net.Session.Instance.IsClient) { r.Fail("enemies are the host's"); yield break; }
            var spot = MapScenarioEnemies.FreeSpot(Centre + new Vector3(F(a[1]), 0f, F(a[2])));
            // spawnCharacterAround takes a non-zero offset times the distance as the position itself.
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, a[0], nocturnal: false, attackPlayer: false);
            if (c == null) { r.Fail($"couldn't spawn {a[0]}"); yield break; }
            c.transform.position = spot;
            _enemy = c;
            _targets.Clear(); _nearerWhile.Clear();
            yield return new WaitForSeconds(1.5f);
            NetEnemies.Instance.NetIdOf(c, out _enemyId);
            Plugin.Instance.StartCoroutine(WatchEnemy(c));
            r.Info = $"{c.name} #{_enemyId} at {Fmt(c.transform.position)}, health {c.health}";
        }

        /// <summary>The host's enemy nearest to our player (this game's copy of it), alive or dead.</summary>
        private static Character HostEnemy(bool alive, float within = 4000f)
        {
            var ne = NetEnemies.Instance;
            if (ne == null) return null;
            return Nearest(Object.FindObjectsOfType<Character>().Where(x => x.alive == alive && NetEnemies.Eligible(x) && ne.NetIdOf(x, out _)
                && StateDump.Flat(x.transform.position - P.transform.position) < within), P.transform.position);
        }

        /// <summary>"mt-hit N DAMAGE [--kill]": walk to the host's enemy, hit it N times, and walk back to where we stood.
        /// It should be alive after; with --kill, hit until it's dead (N at most), and it should be.</summary>
        private static IEnumerator HitEnemy(string[] a, Result r)
        {
            int n = int.Parse(a[0]);
            float dmg = F(a[1]);
            var from = P.transform.position;
            var c = HostEnemy(true);
            if (c == null) { r.Fail("no enemy of the host's near us"); yield break; }
            float was = c.health;
            for (int i = 0; i < n && c != null && c.alive; i++)
            {
                yield return Walk(c.transform.position, 80f);
                AsPlayer(() => c.getHit(dmg, P.transform, false, true, true, true, false, false, false));
                yield return new WaitForSeconds(0.4f);
            }
            bool kill = a.Contains("--kill");
            if (kill) yield return new WaitForSeconds(3f); // a client settles the corpse 2 s after it dies
            else yield return Walk(from, 40f);
            uint id = 0;
            if (c != null) NetEnemies.Instance.NetIdOf(c, out id);
            r.Info = $"{(c != null ? c.name : "-")} #{id}: hits of {dmg:0}, health {was:0} -> {(c != null ? c.health : 0):0}, alive {c != null && c.alive}";
            if (kill && c != null && c.alive) r.Fail($"{r.Info}: still alive");
            if (!kill && (c == null || !c.alive)) r.Fail($"{r.Info}: it died (the test wanted it alive)");
        }

        /// <summary>"mt-target WHO [SECS] [--nearer OTHER]" (host): within SECS (default 15) the enemy goes after WHO's
        /// player. With --nearer OTHER: while it was after WHO, OTHER was nearer to it at some moment (it went by
        /// threat, not by who is closest).</summary>
        private static IEnumerator Target(string[] a, Result r)
        {
            var c = _enemy;
            if (c == null) { r.Fail("no enemy (mt-spawn first)"); yield break; }
            string want = PlayerName(a[0]);
            float secs = a.Length > 1 && !a[1].StartsWith("--") ? F(a[1]) : 15f;
            int ni = Array.IndexOf(a, "--nearer");
            string nearer = ni >= 0 ? PlayerName(a[ni + 1]) : null;
            float until = Time.realtimeSinceStartup + secs;
            while (c != null && c.alive && NameOf(c.target) != want && Time.realtimeSinceStartup < until) yield return null;
            if (nearer != null)
            {
                // Give it a moment after it turned to WHO, to be overtaken by nobody and pass OTHER on the way.
                float more = Time.realtimeSinceStartup + 4f;
                while (c != null && c.alive && Time.realtimeSinceStartup < more && !(_nearerWhile.TryGetValue(want, out var s0) && s0.Contains(nearer))) yield return null;
            }
            r.Info = $"after {NameOf(c != null ? c.target : null)}; threat: {ThreatText(c)}; targets so far: {string.Join(" > ", _targets)}";
            if (c == null || !c.alive) { r.Fail($"{r.Info}: it's dead"); yield break; }
            if (NameOf(c.target) != want) r.Fail($"{r.Info}: not after {want}");
            else if (nearer != null && !(_nearerWhile.TryGetValue(want, out var s) && s.Contains(nearer)))
                r.Fail($"{r.Info}: {nearer} was never nearer to it while it was after {want} (the test shows nothing)");
        }

        /// <summary>"mt-never WHO" (host): the enemy never went after WHO's player.</summary>
        private static IEnumerator Never(string[] a, Result r)
        {
            string who = PlayerName(a[0]);
            r.Info = $"targets: {string.Join(" > ", _targets)}";
            if (_targets.Contains(who)) r.Fail($"{r.Info}: it went after {who}");
            yield break;
        }

        /// <summary>"mt-dead" (any game): our copy of the host's enemy (the one nearest us) is dead and its corpse searchable.</summary>
        private static IEnumerator Dead(string[] a, Result r)
        {
            Character c = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                c = HostEnemy(false);
                if (c != null && c.GetComponent<Item>() != null && c.GetComponentInChildren<Inventory>(true) != null) break;
                yield return new WaitForSeconds(0.25f);
            }
            var alive = HostEnemy(true, 2500f);
            if (c == null) { r.Fail($"no dead enemy of the host's near us (a living one: {(alive != null ? alive.name : "none")})"); yield break; }
            var inv = c.GetComponentInChildren<Inventory>(true);
            NetEnemies.Instance.NetIdOf(c, out uint id);
            r.Info = $"{c.name} #{id} dead at {Fmt(c.transform.position)}, searchable {c.GetComponent<Item>() != null}, loot [{(inv != null ? MapScenarioWorld.Contents(inv) : "-")}]";
            if (c.GetComponent<Item>() == null || inv == null) r.Fail($"{r.Info}: its corpse can't be searched here");
        }

        /// <summary>"mt-corpse [--take]": walk to the host's dead enemy nearest us and open its body; with --take, take
        /// its first thing. Fails if the host refuses.</summary>
        private static IEnumerator Corpse(string[] a, Result r)
        {
            var c = HostEnemy(false);
            var inv = c != null ? c.GetComponentInChildren<Inventory>(true) : null;
            if (c == null || inv == null || c.GetComponent<Item>() == null) { r.Fail($"no searchable corpse near us ({(c != null ? c.name : "none")})"); yield break; }
            yield return Walk(c.transform.position, 90f);
            int mark = MapScenarioIssues.LogMark();
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(1.5f); // the host's contents come back (ContainerSlotsAll)
            string took = "looked";
            if (a.Contains("--take"))
            {
                var slot = inv.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem));
                if (slot == null) took = "nothing in it";
                else
                {
                    var it = new InvItemClass(slot.invItem);
                    int before = MapScenarioWorld.CarriedOf(it.type);
                    AsPlayer(() => { slot.removeItem(); P.Inventory.addItemTypeToPlayer(it.type, it.amount, dropIfNoRoom: true); });
                    yield return new WaitForSeconds(1f);
                    took = $"took {it.type} x{it.amount} (we carry {before} -> {MapScenarioWorld.CarriedOf(it.type)})";
                }
            }
            if (P.openedItemInventory == inv) P.openedItemInventory = null;
            yield return new WaitForSeconds(1.5f);
            var refused = MapScenarioIssues.LogLines().Skip(mark).FirstOrDefault(l => !l.Contains("[map]") && (l.Contains("refused") || l.Contains("is in use by")));
            r.Info = $"{c.name} {ObjRef.Of(inv.gameObject)}: {took}; it holds [{MapScenarioWorld.Contents(inv)}]";
            if (refused != null) r.Fail($"{r.Info}; {refused.Trim()}");
        }

        // ---- 2. one container slot, several players at the same moment ----------------------------------------------

        /// <summary>"mt-grab CONTAINER AT": at epoch second AT, open the container and take its first thing, as the other
        /// players do at the same moment. Replies "got TYPE" or "rolled back TYPE"; fails if what we carry and what it
        /// holds don't agree with that (the item doubled or lost here).</summary>
        private static IEnumerator Grab(string[] a, Result r)
        {
            var inv = MapScenarioWorld.Container(a[0]);
            if (inv == null) { r.Fail($"no container {a[0]}"); yield break; }
            double at = double.Parse(a[1], CultureInfo.InvariantCulture);
            yield return Walk(inv.transform.position);
            if (Now > at) { r.Fail($"walked past the moment ({Now - at:0.0} s late)"); yield break; }
            while (Now < at) yield return null;
            P.setOpenedItemInventory(inv); // as the game opens it
            yield return null; // the game's open: the lock is asked for, the slots watched
            yield return null;
            var slot = inv.slots.FirstOrDefault(s => s != null && !InvItemClass.isNull(s.invItem));
            if (slot == null) { P.openedItemInventory = null; r.Fail($"{inv.name} is empty here"); yield break; }
            var item = new InvItemClass(slot.invItem);
            int si = inv.slots.IndexOf(slot);
            int before = MapScenarioWorld.CarriedOf(item.type);
            AsPlayer(() => { slot.removeItem(); P.Inventory.addItemTypeToPlayer(item.type, item.amount, dropIfNoRoom: true); });
            yield return new WaitForSeconds(3f);
            if (P.openedItemInventory == inv) P.openedItemInventory = null;
            yield return new WaitForSeconds(2f);
            int now = MapScenarioWorld.CarriedOf(item.type);
            bool got = now > before;
            bool back = !InvItemClass.isNull(inv.slots[si].invItem) && inv.slots[si].invItem.type == item.type;
            r.Info = $"{(got ? "got" : "rolled back")} {item.type} x{item.amount} (slot {si} of {inv.name} {MapScenarioWorld.Ref(inv)}): we carry {before} -> {now}; it holds [{MapScenarioWorld.Contents(inv)}]";
            if (now != before && now != before + item.amount) r.Fail($"{r.Info}: carried amount changed by something else");
            // Rolled back: the slot is the host's again, which the winner emptied (or still holds it, if nobody got it).
            if (got && back) r.Fail($"{r.Info}: we got it and it's still in the slot");
        }

        /// <summary>"mt-locks" (host): how many containers someone has open (each released once its player closed or left).</summary>
        private static IEnumerator Locks(string[] a, Result r)
        {
            int n = Containers.Instance != null ? Containers.Instance.LockCount : -1;
            r.Info = $"{n} container(s) held open";
            if (a.Length > 0 && n != int.Parse(a[0])) r.Fail($"{r.Info}, wanted {a[0]}");
            yield break;
        }

        /// <summary>"mt-leave" (client): leave the session (as quitting to the menu does).</summary>
        private static IEnumerator Leave(string[] a, Result r)
        {
            var s = Net.Session.Instance;
            if (s == null || !s.IsClient) { r.Fail("not a client"); yield break; }
            s.Backend.Leave();
            yield return new WaitForSeconds(3f);
            r.Info = "left";
        }

        /// <summary>"mt-open-free CONTAINER": open it and keep it open 3 s: nobody else is said to be using it.</summary>
        private static IEnumerator OpenFree(string[] a, Result r)
        {
            var inv = MapScenarioWorld.Container(a[0]);
            if (inv == null) { r.Fail($"no container {a[0]}"); yield break; }
            yield return Walk(inv.transform.position);
            float t0 = Time.realtimeSinceStartup;
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(3f);
            var msg = MapScenarioWorld.Messages.Where(m => m.at >= t0 && m.msg.Contains("is using this")).Select(m => m.msg).FirstOrDefault();
            if (P.openedItemInventory == inv) P.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{inv.name} {MapScenarioWorld.Ref(inv)}: {msg ?? "free"}";
            if (msg != null) r.Fail(r.Info);
        }

        // ---- 3. one trader, two players ------------------------------------------------------------------------

        /// <summary>"mt-talk [--refused]": talk to the trader nearest the centre (NPC.talkTo) and stay in the dialogue
        /// (talk-end closes it). With --refused, someone else is talking to him: we're told so and ours closes.</summary>
        private static IEnumerator Talk(string[] a, Result r)
        {
            var npc = MapScenarioWorld.Trader();
            if (npc == null) { r.Fail("no trader here"); yield break; }
            bool expectRefused = a.Contains("--refused");
            yield return Walk(npc.transform.position, 150f);
            int mark = MapScenarioIssues.LogMark();
            var win = Singleton<UI>.Instance.dialogueWindow;
            AsPlayer(() => npc.talkTo());
            float until = Time.realtimeSinceStartup + (expectRefused ? 8f : 2.5f);
            bool Told() => MapScenarioIssues.LogLines().Skip(mark).Any(l => !l.Contains("[map]") && (l.Contains("is talking to them") || l.Contains("just talked to them")));
            while (Time.realtimeSinceStartup < until && !(expectRefused && Told() && win.npc != npc)) yield return new WaitForSecondsRealtime(0.25f);
            bool told = Told();
            r.Info = $"{npc.name} {ObjRef.Of(npc.gameObject)}: told someone's talking {told}, dialogue {(win.npc == npc ? "open" : "closed")}";
            if (expectRefused)
            {
                if (!told || win.npc == npc) r.Fail(r.Info);
                if (win.npc == npc) yield return MapScenarioGaps.Close(win, npc);
            }
            else if (told || win.npc != npc) r.Fail(r.Info);
        }

        // ---- 4. things done while a player is far away ---------------------------------------------------------

        /// <summary>"mt-door-drawn": the door nearest the centre is drawn as it is (an open door's body swung away from
        /// where it sits closed, a closed one's not), not only flagged so.</summary>
        private static IEnumerator DoorDrawn(string[] a, Result r)
        {
            var door = Nearest(Object.FindObjectsOfType<Door>().Where(d => !d.destroyed && ObjRef.Of(d.gameObject).Valid), Centre);
            if (door == null || door.body == null) { r.Fail("no door"); yield break; }
            yield return new WaitForSeconds(1f); // let a body that was just switched on settle
            float swung = DoorSwing(door);
            bool drawnOpen = Mathf.Abs(swung) > 15f;
            r.Info = $"{door.name} {ObjRef.Of(door.gameObject)}: open {door.opened}, swung {swung:0} degrees, kinematic {(door.body.GetComponent<Rigidbody>() is Rigidbody rb && rb.isKinematic)}";
            if (drawnOpen != door.opened) r.Fail($"{r.Info}: drawn {(drawnOpen ? "open" : "closed")}");
        }

        /// <summary>How far a door's body has swung on its hinge from where it sits closed (setCloseRotation puts its
        /// local z at 90).</summary>
        internal static float DoorSwing(Door door) => Mathf.DeltaAngle(90f, door.body.localRotation.eulerAngles.z); // how far it has swung from shut (World.DoorLeaf swings a door opened while switched off)

        // ---- 5. night attackers, players in different hideouts -------------------------------------------------

        /// <summary>"mt-hideout-go": walk to the nearest hideout other than the one we're in (or nearest to the centre).</summary>
        private static IEnumerator HideoutGo(string[] a, Result r)
        {
            var wg = Singleton<WorldGenerator>.Instance;
            var here = NightSpawns.BigLocationAt(Centre);
            var to = wg?.locations.Where(l => l != null && l.playerBase && !l.isSubLocation && l != here)
                .OrderBy(l => StateDump.Flat(l.transform.position - Centre)).FirstOrDefault();
            if (to == null) { r.Fail($"no other hideout (the centre's is {(here != null ? here.name : "none")})"); yield break; }
            yield return Walk(to.transform.position, 60f);
            yield return new WaitForSeconds(1.5f);
            var now = NightSpawns.BigLocationAt(P.transform.position);
            r.Info = $"at {Fmt(P.transform.position)}: {(now != null ? now.name : "no location")} (went to {to.name}, {StateDump.Flat(to.transform.position - Centre):0} from the centre)";
            if (now != to) r.Fail($"{r.Info}: not in it");
        }

        /// <summary>"mt-night N" (host): the night's attacker spawn (CharacterSpawner.spawnNightChar) N times. Each
        /// occupied hideout should get one each time (until its budget is spent), aimed at the host's player if they
        /// are in it, else at a joiner there; players sharing a hideout get no extra ones. Then they're removed again.</summary>
        private static IEnumerator Night(string[] a, Result r)
        {
            if (Net.Session.Instance == null || !Net.Session.Instance.IsHost) { r.Fail("night attackers are the host's"); yield break; }
            int n = a.Length > 0 ? int.Parse(a[0]) : 3;
            var spawner = Singleton<CharacterSpawner>.Instance;
            var call = HarmonyLib.AccessTools.Method(typeof(CharacterSpawner), "spawnNightChar");
            P.whereAmI.checkWhereAmI();
            // Who is where, as the host sees it.
            var where = new List<string> { $"host: {Name(P.whereAmI.bigLocation)}" };
            var hideouts = new HashSet<string>();
            if (P.whereAmI.bigLocation != null && P.whereAmI.bigLocation.playerBase) hideouts.Add(P.whereAmI.bigLocation.name);
            foreach (var px in NetPlayerProxy.All.Values.Where(x => x != null && x.alive))
            {
                bool home = NightSpawns.AtHideout(px.transform.position, out var loc);
                where.Add($"{px.OwnerName}: {Name(loc)}{(home ? "" : " (no hideout)")}");
                if (home) hideouts.Add(loc.name);
            }
            var sc = Singleton<NightScenarios>.Instance;
            if (sc.currentScenario == null) sc.setCurrentScenario();
            string budget = sc.currentScenario == null ? "no scenario" :
                sc.currentScenario.name + ": " + string.Join(", ", sc.currentScenario.characters.Where(x => x != null).Select(x => $"{x.characterName} x{x.amount}"));
            int max = sc.currentScenario == null ? 0 : sc.currentScenario.characters.Where(x => x != null && !string.IsNullOrEmpty(x.characterName)).Sum(x => Math.Max(0, x.amount));
            int from = NightSpawns.SentLog.Count;
            bool was = spawner.spawnNocturnalCharacters;
            spawner.spawnNocturnalCharacters = true;
            for (int i = 0; i < n; i++) { call.Invoke(spawner, null); yield return new WaitForSeconds(0.3f); }
            spawner.spawnNocturnalCharacters = was;
            var sent = NightSpawns.SentLog.Skip(from).ToList();
            var per = sent.GroupBy(s => s.Hideout).ToDictionary(g => g.Key, g => g.ToList());
            r.Info = $"{string.Join("; ", where)} | {budget} | " +
                     string.Join("; ", hideouts.Select(h => $"{h}: {(per.TryGetValue(h, out var l) ? l.Count : 0)} ({string.Join(",", per.TryGetValue(h, out var l2) ? l2.Select(s => s.At) : new string[0])})"));
            int want = Math.Min(n, max);
            foreach (var h in hideouts)
            {
                int got = per.TryGetValue(h, out var l) ? l.Count : 0;
                if (got != want) r.Fail($"{r.Info}: {h} got {got} attackers from {n} spawns, wanted {want}");
            }
            foreach (var h in per.Keys) if (!hideouts.Contains(h)) r.Fail($"{r.Info}: attackers sent to {h}, where nobody is");
            if (hideouts.Count < 2) r.Fail($"{r.Info}: wanted players in two hideouts");
            // They'd only fight the players: away with them (the clients' copies go with the host's).
            foreach (var s in sent) if (s.C != null) Object.Destroy(s.C.gameObject);
            yield return new WaitForSeconds(1f);
        }

        private static string Name(Location l) => l != null ? l.name + (l.playerBase ? " (hideout)" : "") : "none";
    }
}
