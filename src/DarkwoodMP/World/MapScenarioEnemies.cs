using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps that fight each kind of enemy and creature in the game (tools/tests/map_scenario_enemies.py):
    /// the host spawns one beside the client's player and watches whom it goes for and what it spawns; the client
    /// waits for it to come and hurt it, then hits it (and whatever it spawned) with the player's own melee until
    /// it's dead, and searches the corpse. Both games' "enemies" dumps are compared after each.
    /// </summary>
    public static class MapScenarioEnemies
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            Add("ee-arena", Arena, timeout: 30f);
            Add("ee-spawn", Spawn, timeout: 15f);
            Add("ee-fight", Fight, timeout: 60f);
            Add("ee-check", Check, timeout: 15f);
            Add("ee-corpse", Corpse, timeout: 20f);
            Add("ee-clear", Clear, timeout: 10f);
            Add("ee-solo", Solo, timeout: 25f);
            Add("ee-shadow-spawn", ShadowSpawn, timeout: 15f);
            Add("ee-flashlight", Flashlight, timeout: 20f);
            Add("ee-shadow-check", ShadowCheck, timeout: 10f);
            Add("ee-flee-activity", FleeActivity, timeout: 15f);
            Add("ee-day", Day, timeout: 10f);
        }

        // ---- the clock -----------------------------------------------------------------------------------------

        private static bool _holdDay;
        /// <summary>The characters there were when the clock was first held: ee-clear leaves these alone.</summary>
        private static readonly HashSet<int> _baseline = new HashSet<int>();

        /// <summary>
        /// "ee-day [off]" (host): the clock set to noon and held there (Controller.DoUpdateTime off, put back if the
        /// game turns it on), so no night (its worms and attackers) comes during the fights; "off" lets it run again.
        /// The clients follow the host's clock (TimeSync).
        /// </summary>
        private static IEnumerator Day(string[] a, Result r)
        {
            var s = Net.Session.Instance;
            if (s != null && s.IsClient) { r.Fail("the clock is the host's"); yield break; }
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null) { r.Fail("no clock"); yield break; }
            if (a.Length > 0 && a[0] == "off")
            {
                _holdDay = false;
                ctl.DoUpdateTime = true;
                r.Info = $"the clock runs again from {ctl.CurrentTime / 60f:0.00} h";
                yield break;
            }
            TestWorld.SetTime(720f);
            if (_baseline.Count == 0) foreach (var c in Object.FindObjectsOfType<Character>()) _baseline.Add(c.GetInstanceID());
            if (!_holdDay) { _holdDay = true; Plugin.Instance.StartCoroutine(HoldDay()); }
            yield return new WaitForSeconds(1.5f);
            r.Info = $"day {ctl.day} {ctl.CurrentTime / 60f:0.00} h, held";
        }

        private static IEnumerator HoldDay()
        {
            while (_holdDay)
            {
                var ctl = Singleton<Controller>.Instance;
                var s = Net.Session.Instance;
                if (ctl == null || s == null || !s.IsHost) { _holdDay = false; break; }
                if (ctl.DoUpdateTime) ctl.DoUpdateTime = false;
                if (ctl.CurrentTime != 720) TestWorld.SetTime(720f);
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ---- what the fights spawn (logged, to find where a thing only one game has came from) -------------------

        /// <summary>While the fights run: things made by name (Core.AddPrefab) are logged with where they came from.</summary>
        internal static bool TraceSpawns;

        internal static void Spawned(GameObject go)
        {
            if (go == null) return;
            string n = go.name;
            if (n.StartsWith("Fov_") || n.Contains("sound_loop") || n.Contains("Spawner"))
            {
                var st = new System.Diagnostics.StackTrace(2, false).GetFrames() ?? new System.Diagnostics.StackFrame[0];
                string from = string.Join(" < ", st.Take(8).Select(f => f.GetMethod()).Where(m => m != null).Select(m => m.DeclaringType?.Name + "." + m.Name));
                string comps = string.Join("/", go.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null).Select(m => m.GetType().Name).Distinct());
                Plugin.Log.LogInfo($"[ee] made {n} at {Fmt(go.transform.position)} (parent {(go.transform.parent != null ? go.transform.parent.name : "none")}; {comps}) from {from}");
            }
        }

        /// <summary>Where the fights are: away from the stations (the host stays at the centre, out of the way).</summary>
        private static Vector3 ArenaSpot => FreeSpot(Centre + new Vector3(0f, 0f, 1500f));

        private static string Short(string kind) => kind.Contains('/') ? kind.Substring(kind.LastIndexOf('/') + 1) : kind;
        private static bool IsKind(Character c, string kind) => c != null && c.name.Replace("(Clone)", "").Trim().Equals(Short(kind), StringComparison.OrdinalIgnoreCase);
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");

        internal static Vector3 FreeSpot(Vector3 at)
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

        /// <summary>The (one) remote player's stand-in on the host.</summary>
        internal static NetPlayerProxy ClientProxy() => NetPlayerProxy.All.Values.FirstOrDefault(p => p != null && p.Owner != Sync.PlayerSync.DummyId);

        internal static void HealNow()
        {
            var p = P;
            if (p == null || !p.alive) return;
            foreach (var b in p.GetComponentsInChildren<Burn>()) if (b != null) b.stop();
            if (p.effects != null) p.effects.removeAllEffects();
            p.bleeding = false;
            if (p.inBearTrap) p.stopAllPerformingActionAnims();
            p.setHealth(p.maxHealth);
        }

        // ---- host --------------------------------------------------------------------------------------------------

        private sealed class Watch
        {
            public string Kind;
            public Character C;
            public uint Id;
            public float SpawnedAt;
            public Vector3 From;
            public readonly HashSet<int> Before = new HashSet<int>();
            public readonly List<Character> Minions = new List<Character>();
            public readonly HashSet<string> Targets = new HashSet<string>();
            public readonly List<string> Timeline = new List<string>();
            public string LastTarget;
            public bool TargetedClient;
            public float Moved;
            public bool Done;
        }
        private static Watch _w;

        /// <summary>"ee-arena" (client): walk to the fighting spot, unhurt.</summary>
        private static IEnumerator Arena(string[] a, Result r)
        {
            TraceSpawns = true;
            yield return Walk(ArenaSpot, 60f);
            HealNow();
            r.Info = $"at {Fmt(P.transform.position)}";
        }

        /// <summary>
        /// "ee-spawn KIND" (host): KIND (a prefab under Characters/) beside the client's player, on the far side from
        /// ours, sent after "the player" (Character.attackPlayer: the one it has most threat for). A kind that is an NPC
        /// here (it runs in every game, not on the host) or has no prefab is reported "skip".
        /// </summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            if (a.Length < 1) { r.Fail("usage: ee-spawn KIND"); yield break; }
            string kind = a[0];
            TraceSpawns = true;
            var proxy = ClientProxy();
            if (proxy == null) { r.Fail("no client player"); yield break; }
            if (Resources.Load("Prefabs/Characters/" + kind) == null) { r.Info = $"skip: no prefab Characters/{kind}"; yield break; }
            var away = proxy.transform.position - P.transform.position; away.y = 0f;
            var spot = FreeSpot(proxy.transform.position + (away.sqrMagnitude > 1f ? away.normalized : Vector3.forward) * 450f);
            var w = new Watch { Kind = kind, SpawnedAt = Time.realtimeSinceStartup };
            foreach (var c0 in Object.FindObjectsOfType<Character>()) w.Before.Add(c0.GetInstanceID());
            Character c;
            try { c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, kind, nocturnal: false, attackPlayer: true); }
            catch (Exception e) { r.Fail($"spawning {kind}: {e.GetType().Name}: {e.Message}"); yield break; }
            if (c == null) { r.Fail($"couldn't spawn {kind}"); yield break; }
            c.transform.position = spot;
            w.C = c; w.From = spot;
            w.Before.Add(c.GetInstanceID());
            _w = w;
            if (c.npc != null) { r.Info = $"skip: {c.name} is an NPC here (runs in every game)"; Object.Destroy(c.gameObject); _w = null; yield break; }
            Plugin.Instance.StartCoroutine(Monitor(w));
            yield return new WaitForSeconds(1f);
            NetEnemies.Instance.NetIdOf(c, out w.Id);
            r.Info = $"{c.name} #{w.Id} at {Fmt(spot)} ({Core.trueDistance(spot, proxy.transform.position):0} from the client, {Core.trueDistance(spot, P.transform.position):0} from us), health {c.health:0}, {Traits(c)}; children: {Children(c)}";
        }

        /// <summary>
        /// "ee-solo KIND" (host): KIND beside our own player, the client far off: whether it hurts our player in the
        /// game's own single-player way within 15 s (to tell a kind that never attacks from one that can't reach a
        /// joiner). It's killed after.
        /// </summary>
        private static IEnumerator Solo(string[] a, Result r)
        {
            string kind = a.Length > 0 ? a[0] : "Spider01";
            var spot = FreeSpot(P.transform.position + new Vector3(0f, 0f, -400f));
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, kind, nocturnal: false, attackPlayer: true);
            if (c == null) { r.Fail($"couldn't spawn {kind}"); yield break; }
            c.transform.position = spot;
            float lost = 0f, hp = P.health, until = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < until && lost <= 0f && c != null && c.alive)
            {
                if (P.health < hp) lost += hp - P.health;
                hp = P.health;
                yield return new WaitForSeconds(0.1f);
            }
            r.Info = $"{kind} beside our player: it hurt us for {lost:0}; {(c != null ? $"{Core.trueDistance(c.transform.position, P.transform.position):0} from us, alive {c.alive}" : "gone")}";
            HealNow();
            if (c != null) { var cc = c; AsPlayer(() => cc.getHit(cc.health + 1000f, P.transform, false, true, false)); }
            yield return new WaitForSeconds(1f);
            if (c != null) Object.Destroy(c.gameObject);
        }

        /// <summary>A character's child objects that are items (with their components): what the dumps' items list shows.</summary>
        internal static string Children(Character c)
        {
            var l = new List<string>();
            foreach (var it in c.GetComponentsInChildren<Item>(true))
                if (it != null && it.gameObject != c.gameObject)
                    l.Add($"{it.name}{(it.gameObject.activeInHierarchy ? "" : " (off)")} [{string.Join("/", it.GetComponents<MonoBehaviour>().Where(m => m != null).Select(m => m.GetType().Name + (m.enabled ? "" : "-off")))}]");
            return l.Count == 0 ? "no items" : string.Join(", ", l);
        }

        /// <summary>What's special about a character (the components and flags behind its special behaviours).</summary>
        private static string Traits(Character c)
        {
            var t = new List<string>();
            if (c.flier != null) t.Add("flier");
            if (c.banshee) t.Add("banshee");
            if (c.summonsAfterEscaping) t.Add("summons");
            if (c.GetComponent<Shooter>() != null) t.Add("shooter");
            if (c.GetComponent<ShadowArmor>() != null) t.Add("shadowArmor");
            if (c.hasPreDeath) t.Add("preDeath");
            if (c.attacks != null) t.Add("attacks " + string.Join("/", c.attacks.Where(x => x != null).Select(x => x.name)));
            t.Add("aggr " + c.aggressiveness);
            if (c.immobile) t.Add("immobile");
            if (c.dontMove) t.Add("dontMove");
            if (c.onlyAttackPlayer) t.Add("onlyAttackPlayer");
            if (c.constantlyAttackPlayer) t.Add("constantlyAttackPlayer");
            if (c.damagesAroundMe) t.Add("damagesAroundMe");
            if (c.dieOnContactWithTarget) t.Add("dieOnContact");
            if (c.sleeping) t.Add("sleeping");
            t.Add("behaviour " + c.behaviour);
            t.Add($"view {c.farViewDistance}/{c.nearViewDistance} x{c.aniSightRangeModifier:0.##}");
            if (c.currentActivity != null && c.currentActivity.isActive) t.Add("activity " + c.currentActivity.type);
            if (c.activities != null && c.activities.Count > 0) t.Add("activities " + string.Join("/", c.activities.Select(x => x.type)));
            t.Add("components " + string.Join("/", c.GetComponents<MonoBehaviour>().Where(m => m != null).Select(m => m.GetType().Name).Distinct()));
            return string.Join(", ", t);
        }

        /// <summary>Host, while the fight lasts: whom the enemy goes for, how far it moved, and what it spawned.</summary>
        private static IEnumerator Monitor(Watch w)
        {
            var proxy = ClientProxy();
            while (!w.Done && Time.realtimeSinceStartup - w.SpawnedAt < 200f)
            {
                var c = w.C;
                if (c != null)
                {
                    if (c.target != null)
                    {
                        var cb = c.target.GetComponent<CharBase>();
                        string who = cb is NetPlayerProxy np ? "client " + np.OwnerName : cb is Player ? "host player" : c.target.name;
                        w.Targets.Add(who);
                        if (who != w.LastTarget) w.Timeline.Add($"{Time.realtimeSinceStartup - w.SpawnedAt:0.0}s {who} ({c.behaviour})");
                        w.LastTarget = who;
                        if (cb is NetPlayerProxy) w.TargetedClient = true;
                    }
                    w.Moved = Mathf.Max(w.Moved, Core.trueDistance(c.transform.position, w.From));
                }
                foreach (var o in Object.FindObjectsOfType<Character>())
                {
                    if (o == null || w.Before.Contains(o.GetInstanceID()) || o.npc != null) continue;
                    w.Before.Add(o.GetInstanceID());
                    if (Core.trueDistance(o.transform.position, w.From) < 3000f) w.Minions.Add(o);
                }
                yield return new WaitForSeconds(0.2f);
            }
        }

        /// <summary>
        /// "ee-check [--targets]" (host): the enemy and all it spawned are dead here; with --targets, it went for the
        /// client's player. Anything still alive is killed (so it doesn't spoil the next fight) and fails the step.
        /// </summary>
        private static IEnumerator Check(string[] a, Result r)
        {
            var w = _w;
            if (w == null) { r.Info = "nothing spawned (skipped)"; yield break; }
            yield return new WaitForSeconds(0.5f);
            w.Done = true;
            var all = new List<Character> { w.C }.Concat(w.Minions).ToList();
            var alive = all.Where(c => c != null && NetEnemies.InPlay(c)).ToList();
            string minions = w.Minions.Count == 0 ? "none" : string.Join(", ", w.Minions.Select(m => m == null ? "(gone)" : $"{m.name}{(NetEnemies.Instance.NetIdOf(m, out uint i) ? " #" + i : "")} {(NetEnemies.InPlay(m) ? "alive" : "dead")}"));
            r.Info = $"{w.Kind}: went for [{string.Join(", ", w.Targets)}], moved {w.Moved:0}, spawned: {minions}, " +
                     (w.C == null ? "gone" : $"alive {w.C.alive}, health {w.C.health:0}, children {Children(w.C)}");
            if (w.Timeline.Count > 1) r.Info += $"; targets: {string.Join(", ", w.Timeline.Take(8))}";
            if (a.Contains("--targets") && !w.TargetedClient) r.Fail($"it never went for the client's player: {r.Info}");
            if (alive.Count > 0)
            {
                r.Fail($"still alive here: {string.Join(", ", alive.Select(c => c.name))}; {r.Info}");
                foreach (var c in alive) AsPlayer(() => c.getHit(c.health + 1000f, P.transform, false, true, false));
            }
        }

        /// <summary>"ee-clear" (host): what the last fight left (corpses, spawned things) removed in every game.</summary>
        private static IEnumerator Clear(string[] a, Result r)
        {
            var w = _w;
            _w = null;
            int n = 0;
            var gone = new HashSet<Character>();
            if (w != null)
                foreach (var c in new List<Character> { w.C }.Concat(w.Minions))
                    if (c != null && gone.Add(c)) { Object.Destroy(c.gameObject); n++; }
            // And whatever else came while the fights went on (a banshee's babies, the world's own animals), near the
            // client: anything not here when the clock was first held.
            var proxy = ClientProxy();
            var others = new List<string>();
            if (proxy != null && _baseline.Count > 0)
                foreach (var c in Object.FindObjectsOfType<Character>())
                    if (c != null && c.npc == null && !gone.Contains(c) && !_baseline.Contains(c.GetInstanceID())
                        && Core.trueDistance(c.transform.position, proxy.transform.position) < 3000f && gone.Add(c))
                    { others.Add(c.name); Object.Destroy(c.gameObject); n++; }
            yield return new WaitForSeconds(1.5f);
            r.Info = $"removed {n}" + (others.Count > 0 ? $" (and of others: {string.Join(", ", others)})" : "");
        }

        // ---- client ------------------------------------------------------------------------------------------------

        private static uint _fought;     // client: the net ID of the kind we fought

        /// <summary>A copy of one of the host's enemies here (and its net ID).</summary>
        private static IEnumerable<(Character c, uint id)> HostEnemies()
        {
            var ne = NetEnemies.Instance;
            if (ne == null) yield break;
            foreach (var c in Object.FindObjectsOfType<Character>())
                if (c != null && ne.NetIdOf(c, out uint id)) yield return (c, id);
        }

        /// <summary>
        /// "ee-fight KIND [--hurts] [--moves]" (client): the host's KIND shows here; with --moves it moves, with
        /// --hurts it hurts our player (waited for up to 12 s, standing still; with --twice, two hits: one pooled
        /// sensor must hurt again), with --throws one of its projectiles shows here (EnemyProjectile or EnemySensorThrown). Then our player hits it, and anything
        /// of the host's it spawned near us, with a pitchfork until none are left alive (healed when low).
        /// </summary>
        private static IEnumerator Fight(string[] a, Result r)
        {
            var p = P;
            string kind = a.Length > 0 ? a[0] : "";
            bool hurts = a.Contains("--hurts"), moves = a.Contains("--moves"), throws = a.Contains("--throws");
            int needHits = a.Contains("--twice") ? 2 : 1;
            int shown0 = EnemyLandings.Shown;
            Character c = null; uint id = 0;
            var earlier = new HashSet<uint>(HostEnemies().Where(e => !IsKind(e.c, kind)).Select(e => e.id)); // the world's own, here before it: not ours to fight
            float until = Time.realtimeSinceStartup + 8f;
            while (c == null && Time.realtimeSinceStartup < until)
            {
                foreach (var (o, i) in HostEnemies())
                    if (IsKind(o, kind) && NetEnemies.InPlay(o) && (c == null || Core.trueDistance(o.transform.position, p.transform.position) < Core.trueDistance(c.transform.position, p.transform.position))) { c = o; id = i; }
                if (c == null) yield return new WaitForSeconds(0.3f);
            }
            if (c == null) { r.Fail($"no copy of the host's {kind} here"); yield break; }
            _fought = id;
            Vector3 from = c.transform.position;
            float moved = 0f, lost = 0f, hp = p.health, closest = float.MaxValue;
            int hits = 0;
            // Let it come and attack.
            float waitUntil = Time.realtimeSinceStartup + (hurts ? (needHits > 1 ? 18f : 12f) : moves ? 6f : 1f);
            while (Time.realtimeSinceStartup < waitUntil && c != null && NetEnemies.InPlay(c))
            {
                moved = Mathf.Max(moved, Core.trueDistance(c.transform.position, from));
                closest = Mathf.Min(closest, Core.trueDistance(c.transform.position, p.transform.position));
                if (p.health < hp) { lost += hp - p.health; hits++; }
                hp = p.health;
                if (p.health < p.maxHealth * 0.5f) { HealNow(); hp = p.health; }
                if (hurts && hits >= needHits && (!moves || moved > 30f) && (!throws || EnemyLandings.Shown > shown0)) break;
                yield return new WaitForSeconds(0.1f);
            }
            int thrown = EnemyLandings.Shown - shown0;
            string came = $"{c.name} #{id}: moved {moved:0}, came within {closest:0}, hurt us {hits} time(s) for {lost:0}, {thrown} projectile(s) shown (children here: {Children(c)})";
            string weapon = a.Contains("--cut") ? CutWeapon() : "pitchfork";
            if (weapon == null) { r.Fail("no melee weapon in the game cuts in half"); yield break; }
            yield return TakeOut(p, weapon, r);
            if (r.Failure != null) yield break;
            // Hit it and whatever of the host's is alive near us until nothing is left: a swing of the weapon (the
            // game's own melee hit, when it lands), then hits through the same path (Character.getHit by our player,
            // which goes to the host as EnemyHit) for the most the weapon does (the Strong skill doubles it), so a
            // fight ends in seconds even with something that dodges, flies or runs.
            var killed = new HashSet<string>();
            var seen = new HashSet<uint> { id };
            var swung = new HashSet<uint>();
            int swings = 0, direct = 0;
            float dmg = HitDamage(p);
            bool cut = a.Contains("--cut");
            float giveUp = Time.realtimeSinceStartup + FightTime, lastSeen = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < giveUp)
            {
                (Character c, uint id) best = default; float bestD = float.MaxValue;
                foreach (var e in HostEnemies())
                {
                    if (!NetEnemies.InPlay(e.c) || earlier.Contains(e.id)) continue;
                    float d = Core.trueDistance(e.c.transform.position, p.transform.position);
                    if (d > 2500f) continue;
                    seen.Add(e.id);
                    if (d < bestD) { bestD = d; best = e; }
                }
                if (best.c == null)
                {
                    // Nothing left: unless something it left (a half cut off, a minion) shows up within a moment.
                    if (Time.realtimeSinceStartup - lastSeen > (a.Contains("--minions") ? 8f : 3f)) break;
                    yield return new WaitForSeconds(0.3f);
                    continue;
                }
                lastSeen = Time.realtimeSinceStartup;
                if (p.health < p.maxHealth * 0.5f) HealNow();
                if (p.health < hp) { lost += hp - p.health; hits++; }
                bool first = swung.Add(best.id);
                if (bestD > (first ? 90f : 300f)) yield return Walk(best.c.transform.position, first ? 70f : 250f, detours: 0);
                if (p.health < hp) { lost += hp - p.health; hits++; }
                if (best.c == null || !NetEnemies.InPlay(best.c)) continue;
                if (first || (cut && best.id == id)) { Swing(p, best.c.transform.position); swings++; yield return new WaitForSeconds(0.45f); }
                else
                {
                    var bc = best.c;
                    AsPlayer(() => bc.getHit(dmg, p.transform, false, true, true, true, false, false, false));
                    direct++;
                    yield return new WaitForSeconds(0.25f);
                }
                hp = p.health;
            }
            var left = HostEnemies().Where(e => NetEnemies.InPlay(e.c) && !earlier.Contains(e.id) && Core.trueDistance(e.c.transform.position, p.transform.position) < 2500f).Select(e => $"{e.c.name} #{e.id}").ToList();
            var others = seen.Where(x => x != id).ToList();
            HealNow();
            r.Info = $"{came}; then {swings} swing(s) and {direct} hit(s) of {dmg:0}; the host's here: {seen.Count} ({(others.Count == 0 ? "nothing it spawned" : "spawned " + string.Join(", ", HostEnemies().Where(e => others.Contains(e.id)).Select(e => $"{e.c.name} #{e.id}")))}), our player lost {lost:0} in all";
            if (moves && moved < 30f) r.Fail($"it didn't move here: {r.Info}");
            else if (hurts && lost <= 0f) r.Fail($"it never hurt our player: {r.Info}");
            else if (hurts && hits < needHits) r.Fail($"it hurt our player only {hits} time(s), not {needHits}: {r.Info}");
            else if (throws && thrown == 0) r.Fail($"none of its projectiles showed here: {r.Info}");
            else if (left.Count > 0) r.Fail($"still alive: {string.Join(", ", left)}; {r.Info}");
        }

        /// <summary>Take a weapon of this type out (made in an empty hotbar slot), as the game's hotbar select does.</summary>
        /// <summary>A melee weapon that can cut a character in half (InvItem.canCutInHalf), from the game's item list.</summary>
        private static string CutWeapon()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null) return null;
            foreach (var type in db.itemsDict.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                InvItem it = null;
                try { it = db.getItem(type, instantiate: false); } catch { }
                if (it != null && it.canCutInHalf && it.isMelee) return type;
            }
            return null;
        }

        /// <summary>How long a fight lasts at most once the hitting starts (s).</summary>
        private const float FightTime = 25f;

        /// <summary>What one hit of the weapon in hand does at most: its damage with upgrades, doubled by the Strong skill (within NetEnemies.DamageCap).</summary>
        private static float HitDamage(Player p)
        {
            var it = p.currentItem;
            if (InvItemClass.isNull(it) || it.baseClass == null) return 10f;
            return Mathf.Max(10f, Mathf.Max(it.getModdedDamage(it.baseClass.damage), it.getModdedDamage(it.baseClass.specialDamage)) * 2f);
        }

        private static IEnumerator TakeOut(Player p, string type, Result r)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!InvItemClass.isNull(p.currentItem) && p.currentItem.type == type && !p.switchingItem) yield break;
                var slot = p.Hotbar.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == type)
                           ?? p.Hotbar.slots.FirstOrDefault(s => InvItemClass.isNull(s.invItem))
                           ?? p.Hotbar.slots.FirstOrDefault(s => !s.selected) ?? p.Hotbar.slots[0];
                slot.inventory = p.Hotbar;
                // A full hotbar (earlier steps left a torch and a flashlight in it): make room in a slot that isn't
                // in hand, or the selected weapon was taken out again instead.
                if (!InvItemClass.isNull(slot.invItem) && slot.invItem.type != type) slot.removeItem();
                // A weapon wears out over the fights: a fresh one when it's worn (a broken one can't be taken out).
                if (!InvItemClass.isNull(slot.invItem) && slot.invItem.type == type && slot.invItem.baseClass != null && slot.invItem.baseClass.hasDurability
                    && slot.invItem.durability < slot.invItem.baseClass.maxDurability * 0.5f)
                    slot.invItem.durability = slot.invItem.baseClass.maxDurability;
                if (InvItemClass.isNull(slot.invItem)) slot.createItem(type, 1);
                foreach (var s in p.Hotbar.slots) s.deselect();
                slot.select(noiseless: true);
                float t = Time.realtimeSinceStartup + 2.5f;
                while ((InvItemClass.isNull(p.currentItem) || p.currentItem.type != type || p.switchingItem) && Time.realtimeSinceStartup < t) yield return null;
            }
            if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != type)
                r.Fail($"couldn't take the {type} out (holding {(InvItemClass.isNull(p.currentItem) ? "nothing" : p.currentItem.type)}, switching {p.switchingItem}; hotbar: " +
                       string.Join(", ", p.Hotbar.slots.Select(s => InvItemClass.isNull(s.invItem) ? "-" : $"{s.invItem.type} ({s.invItem.durability:0}){(s.selected ? " selected" : "")}")) + ")");
        }

        private static readonly System.Reflection.MethodInfo AttackMethod = AccessTools.Method(typeof(Player), "attack", new[] { typeof(float) });
        private static void Swing(Player p, Vector3 at)
        {
            var d = at - p.transform.position; d.y = 0f;
            if (d.sqrMagnitude > 1f) p.transform.rotation = Quaternion.Euler(90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
            AsPlayer(() => AttackMethod.Invoke(p, new object[] { 1f }));
        }

        /// <summary>"ee-corpse" (client): search the body of what we fought, if the game makes it searchable; take what's in it.</summary>
        private static IEnumerator Corpse(string[] a, Result r)
        {
            var p = P;
            var c = NetEnemies.Instance?.CharacterOf(_fought);
            if (c == null) { r.Info = $"#{_fought} is gone here (no corpse)"; yield break; }
            if (NetEnemies.InPlay(c)) { r.Fail($"{c.name} #{_fought} is still alive here"); yield break; }
            yield return new WaitForSeconds(2.5f); // settled (NetEnemies.DeathAnimTime)
            var item = c.GetComponent<Item>();
            var inv = c.GetComponentInChildren<Inventory>(true);
            if (item == null || inv == null) { r.Info = $"{c.name} #{_fought}: not searchable (Item {item != null}, Inventory {inv != null})"; yield break; }
            yield return Walk(c.transform.position, 90f);
            int mark = MapScenarioIssues.LogMark();
            p.openedItemInventory = inv;
            yield return new WaitForSeconds(1.5f);
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
            var refused = MapScenarioIssues.LogLines().Skip(Math.Max(0, mark)).FirstOrDefault(l => l.Contains("refused"));
            r.Info = $"{c.name} #{_fought}: {took} (children {Children(c)})";
            if (refused != null) r.Fail($"{r.Info}; {refused}");
        }

        // ---- P9: shadow armour and a joiner's flashlight ------------------------------------------------------------

        private static ShadowArmor _armor;
        private static float _armorHealth;

        /// <summary>"ee-shadow-spawn" (host): a Dog with shadow armour beside the client's player, asleep in the dark.</summary>
        private static IEnumerator ShadowSpawn(string[] a, Result r)
        {
            var proxy = ClientProxy();
            if (proxy == null) { r.Fail("no client player"); yield break; }
            var away = proxy.transform.position - P.transform.position; away.y = 0f;
            var spot = FreeSpot(proxy.transform.position + away.normalized * 300f);
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, "Dog", nocturnal: false, attackPlayer: false);
            if (c == null) { r.Fail("couldn't spawn a dog"); yield break; }
            c.transform.position = spot;
            c.immobile = true; // it stays where it is, for the client's beam to find it
            _armor = c.gameObject.AddComponent<ShadowArmor>();
            _beamLog = "";
            Plugin.Instance.StartCoroutine(BeamWatch(_armor));
            yield return new WaitForSeconds(1.5f);
            _armorHealth = _armor.health;
            bool lit = Core.isInLight(spot, mustBeWalkable: false);
            r.Info = $"{c.name} at {Fmt(spot)}, shadow armour {_armorHealth:0}, lit here {lit}";
            if (lit) r.Fail($"its spot is lit already: {r.Info}");
        }

        private static string _beamLog = "";

        /// <summary>Host, until ee-shadow-check: how the client's beam (as ShadowArmorJoinerLightPatch sees it) stood to the dog.</summary>
        private static IEnumerator BeamWatch(ShadowArmor armor)
        {
            int samples = 0, on = 0, inBox = 0, lit = 0;
            string closest = "none";
            float best = float.MaxValue;
            while (armor != null && _armor == armor)
            {
                samples++;
                var proxy = ClientProxy();
                var ps = Sync.PlayerSync.Instance;
                if (Core.isInLight(armor.transform.position, mustBeWalkable: false)) lit++;
                if (proxy != null && ps != null && ps.Remotes.TryGetValue(proxy.Owner, out var rp) && rp.Root != null && rp.Last != null && rp.Last.Flashlight)
                {
                    on++;
                    var pos = armor.transform.position;
                    bool inside = ShadowArmorJoinerLightPatch.InBeam(rp.Root.transform, pos, out float ahead, out float aside);
                    if (inside) inBox++;
                    if (aside < best) { best = aside; closest = FormattableString.Invariant($"{ahead:0} ahead, {aside:0} aside; puppet yaw {rp.Root.transform.eulerAngles.y:0} rot {rp.Root.transform.eulerAngles.x:0}"); }
                }
                _beamLog = $"beam watch: {samples} samples, client's beam on in {on}, dog in its beam in {inBox}, lit in {lit}; nearest: {closest}";
                yield return new WaitForSeconds(0.25f);
            }
        }

        /// <summary>"ee-flashlight" (client): our flashlight on, aimed at the host's shadow-armoured dog for a few seconds, then off.</summary>
        private static IEnumerator Flashlight(string[] a, Result r)
        {
            var p = P;
            var dog = HostEnemies().Where(e => e.c.name.StartsWith("Dog")).OrderBy(e => Core.trueDistance(e.c.transform.position, p.transform.position)).Select(e => e.c).FirstOrDefault();
            if (dog == null) { r.Fail("no copy of the host's dog here"); yield break; }
            yield return TakeOut(p, "flashlight", r);
            if (r.Failure != null) yield break;
            var beam = p.transform.Find("Flashlight");
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until)
            {
                var d = dog.transform.position - p.transform.position; d.y = 0f;
                if (d.magnitude > 250f) yield return Walk(dog.transform.position, 160f, detours: 0);
                d = dog.transform.position - p.transform.position; d.y = 0f;
                if (d.sqrMagnitude > 1f) p.transform.rotation = Quaternion.Euler(90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
                if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != "flashlight") break;
                yield return new WaitForSeconds(0.1f);
            }
            r.Info = $"flashlight {(beam != null ? (beam.gameObject.activeSelf ? "on" : "off") : "?")} at {dog.name} {Core.trueDistance(dog.transform.position, p.transform.position):0} away";
            if (beam == null || !beam.gameObject.activeSelf) r.Fail($"our flashlight isn't on: {r.Info}");
        }

        /// <summary>"ee-shadow-check" (host): the client's flashlight took the shadow armour's health and set the dog on the client.</summary>
        private static IEnumerator ShadowCheck(string[] a, Result r)
        {
            if (_armor == null) { r.Fail("no shadow armour (ee-shadow-spawn first)"); yield break; }
            var c = _armor.GetComponent<Character>();
            var target = c != null && c.target != null ? c.target.GetComponent<CharBase>() : null;
            r.Info = $"shadow armour {_armorHealth:0} -> {_armor.health:0}; the dog goes for {(target is NetPlayerProxy np ? "client " + np.OwnerName : target is Player ? "host player" : target != null ? target.name : "nobody")}";
            bool hurt = _armor.health < _armorHealth;
            r.Info += "; " + _beamLog;
            var armor = _armor;
            _armor = null;
            if (c != null) { var cc = c; AsPlayer(() => cc.getHit(cc.health + 1000f, P.transform, false, true, false)); }
            yield return new WaitForSeconds(1f);
            if (c != null) Object.Destroy(c.gameObject);
            if (!hurt) r.Fail($"the client's flashlight didn't hurt it: {r.Info}");
            else if (!(target is NetPlayerProxy)) r.Fail($"it didn't go for the client: {r.Info}");
        }

        // ---- P8: a scripted run-away activity ---------------------------------------------------------------------

        /// <summary>
        /// "ee-flee-activity" (host): a Chicken beside the client, given the game's runAway activity with no target
        /// (as a scripted event does), runs from the client's player (the nearest), not from ours behind it.
        /// </summary>
        private static IEnumerator FleeActivity(string[] a, Result r)
        {
            var proxy = ClientProxy();
            if (proxy == null) { r.Fail("no client player"); yield break; }
            var host = P.transform.position;
            var toClient = proxy.transform.position - host; toClient.y = 0f;
            var spot = FreeSpot(proxy.transform.position - toClient.normalized * 250f); // between the two players, nearer the client
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, "Chicken", nocturnal: false, attackPlayer: false);
            if (c == null) { r.Fail("couldn't spawn a chicken"); yield break; }
            c.transform.position = spot;
            yield return new WaitForSeconds(1f);
            var act = new Character.Activity { type = Character.Activity.Type.runAway };
            act.run(c);
            yield return new WaitForSeconds(3f);
            var now = c.transform.position;
            float fromClient = Core.trueDistance(now, proxy.transform.position) - Core.trueDistance(spot, proxy.transform.position);
            float fromHost = Core.trueDistance(now, host) - Core.trueDistance(spot, host);
            r.Info = $"{c.name} from {Fmt(spot)} to {Fmt(now)}: {fromClient:0} further from the client, {fromHost:0} further from us";
            var cc = c; AsPlayer(() => cc.getHit(cc.health + 1000f, P.transform, false, true, false));
            yield return new WaitForSeconds(1f);
            if (c != null) Object.Destroy(c.gameObject);
            if (!(fromClient > 100f)) r.Fail($"it didn't run from the client: {r.Info}");
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class EnemyTestSpawnTracePatch
    {
        static void Postfix(GameObject __result) { if (MapScenarioEnemies.TraceSpawns) MapScenarioEnemies.Spawned(__result); }
    }
}
