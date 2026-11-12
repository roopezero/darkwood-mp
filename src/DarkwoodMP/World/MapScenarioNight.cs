using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for what the night brings (tools/tests/map_scenario_night.py) and for how smoothly a client
    /// draws the host's enemies.
    ///   nl-inventory (host)   every night scenario's events and what their steps do, in the log ([night] lines).
    ///   nl-event NAME (host)  the night event NAME fired at the nearest hideout, as the night fires it.
    ///   nl-herd N (host)      N enemies that run at the client's player, by it.
    ///   nl-motion SECS        (client) walks in a circle for SECS while counting how its copies of the host's
    ///                         enemies are drawn (EnemyMotion.Stats).
    ///   nl-clear (host)       the herd removed.
    /// Dump section "nightobjects": every chunk's night-spawned objects (WorldChunk.nightSpawnedObjects, gone at
    /// dawn) and every object of a kind a night event's spawn step makes, wherever they are, keyed by name and place.
    /// </summary>
    public static class MapScenarioNight
    {
        private static Player P => Player.Instance;
        private static readonly List<Character> _herd = new List<Character>();

        public static void Register()
        {
            MapScenario.Add("nl-inventory", Inventory, timeout: 20f);
            MapScenario.Add("nl-event", FireEvent, timeout: 20f);
            MapScenario.Add("nl-herd", Herd, timeout: 20f);
            MapScenario.Add("nl-motion", Motion, timeout: 90f);
            MapScenario.Add("nl-clear", Clear, timeout: 10f);

            StateDump.AddSection("nightobjects", ctx =>
            {
                var names = NightObjectNames();
                var listed = new HashSet<GameObject>();
                foreach (var ch in StateDump.All<WorldChunk>())
                    foreach (var go in ch.nightSpawnedObjects)
                        if (go != null && listed.Add(go)) Entry(ctx, go, true);
                foreach (var t in StateDump.All<Transform>())
                    if (t != null && names.Contains(Name(t.gameObject)) && !listed.Contains(t.gameObject)) Entry(ctx, t.gameObject, false);
            });
        }

        private static void Entry(StateDump.Ctx ctx, GameObject go, bool listed)
        {
            var p = go.transform.position;
            string name = Name(go);
            ctx.Set($"{name} {Mathf.Round(p.x / 10f) * 10f:0},{Mathf.Round(p.z / 10f) * 10f:0}", new Dictionary<string, object>
            {
                ["name"] = name, ["pos"] = StateDump.Pos(p), ["listed"] = listed,
            });
        }

        private static string Name(GameObject g) => g.name.Replace("(Clone)", "").Trim();

        private static IEnumerator None() { yield break; }

        private static IEnumerable<CustomEvent> NightEvents(NightScenario sc) =>
            sc.customEvents.Concat(sc.customEventAndInts.Where(x => x != null).Select(x => x.customEvent)).Where(e => e != null).Distinct();

        /// <summary>The prefabs the night events' spawn steps make.</summary>
        private static HashSet<string> NightObjectNames()
        {
            if (_names != null) return _names;
            var names = new HashSet<string>();
            var ns = Singleton<NightScenarios>.Instance;
            if (ns == null) return names;
            foreach (var sc in ns.scenarios.Where(x => x != null))
                foreach (var ce in NightEvents(sc).Where(e => e.theEvent != null))
                    foreach (var ge in ce.theEvent.gameEvents.Where(g => g != null))
                        foreach (var e in ge.events)
                            if (e != null && e.type == GameEvent.Type.gameObject && e.gameObjectModifyType == GameEvent.GameObjectModify.spawn && e.targetTransform != null)
                                names.Add(e.targetTransform.name);
            return _names = names;
        }
        private static HashSet<string> _names;

        // ---- what the night events do ----------------------------------------------------------------------------

        /// <summary>"nl-inventory": each night scenario's events, and for each the kinds of steps it runs.</summary>
        private static IEnumerator Inventory(string[] a, MapScenario.Result r)
        {
            var ns = Singleton<NightScenarios>.Instance;
            if (ns == null) { r.Fail("no NightScenarios"); return None(); }
            var kinds = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var seen = new HashSet<CustomEvent>();
            int events = 0;
            foreach (var sc in ns.scenarios)
            {
                if (sc == null) continue;
                foreach (var ce in NightEvents(sc))
                {
                    if (!seen.Add(ce)) continue;
                    events++;
                    var re = ce.theEvent;
                    if (re == null) { Plugin.Log.LogInfo($"[night] {sc.name} / {ce.name}: no RandomEvent"); continue; }
                    var steps = new List<string>();
                    if (re.type == RandomEvent.Type.locationEvent)
                        foreach (var ge in re.gameEvents)
                        {
                            if (ge == null) continue;
                            foreach (var e in ge.events)
                            {
                                if (e == null || e.disabled) continue;
                                string k = Kind(e);
                                steps.Add(k);
                                if (!WorldEvents.PresentationOnly(e))
                                {
                                    if (!kinds.TryGetValue(k, out var set)) kinds[k] = set = new SortedSet<string>(StringComparer.Ordinal);
                                    set.Add(ce.name);
                                }
                            }
                        }
                    else steps.Add("(" + re.type + (string.IsNullOrEmpty(re.Value) ? "" : " " + re.Value) + ")");
                    Plugin.Log.LogInfo($"[night] {sc.name} (difficulty {sc.difficulty}) / {ce.name}: {re.type}: {string.Join("; ", steps)}");
                }
            }
            foreach (var kv in kinds) Plugin.Log.LogInfo($"[night] world step {kv.Key}: in {string.Join(", ", kv.Value)}");
            r.Info = $"{events} night events; world steps: {string.Join(" | ", kinds.Select(kv => $"{kv.Key} x{kv.Value.Count}"))}";
            return None();
        }

        private static string Kind(GameEvent e)
        {
            string k = e.type.ToString();
            switch (e.type)
            {
                case GameEvent.Type.gameObject:
                    k += " " + e.gameObjectModifyType + (e.gameObjectModifyType == GameEvent.GameObjectModify.spawn && e.targetTransform != null ? " " + e.targetTransform.name : "");
                    break;
                case GameEvent.Type.item:
                    k += " " + e.itemModifyType + (e.itemModifyType == GameEvent.ItemModify.light ? " " + e.lightModify : "");
                    break;
                case GameEvent.Type.runFunction: k += " " + e.Value; break;
                case GameEvent.Type.spawnCharacter: k += " " + e.Value + " " + e.spawnCharacterType; break;
                case GameEvent.Type.modifyDoor: k += " " + e.doorModifyType; break;
                case GameEvent.Type.modifyCharacter: k += " " + e.characterModifyType; break;
                case GameEvent.Type.modifyMainScript: k += " " + e.Value; break;
                case GameEvent.Type.switchItemOnOff: k += e.activeModifier ? " on" : " off"; break;
                case GameEvent.Type.setSprite: k += " " + e.Value; break;
            }
            var targets = e.locationObjects.Where(o => o != null).Select(o => o.type.ToString())
                .Concat(e.targetGameObjects.Where(g => g != null).Select(g => g.name)).Distinct().Take(4).ToList();
            if (e.getTargetFromLocation || targets.Count > 0) k += " [" + (e.getTargetFromLocation ? "location " : "") + string.Join(",", targets) + "]";
            return k;
        }

        // ---- a night event at our hideout ------------------------------------------------------------------------

        /// <summary>
        /// "nl-event NAME" (host): the night event NAME (a CustomEvent of any scenario) fired as the night fires it,
        /// at the hideout nearest to us (the game puts its GameEvents under the player's big location). Waits for
        /// what its steps make (NightObjects).
        /// </summary>
        private static IEnumerator FireEvent(string[] a, MapScenario.Result r)
        {
            var p = P;
            var ns = Singleton<NightScenarios>.Instance;
            if (p == null || ns == null || a.Length < 1) { r.Fail("usage: nl-event NAME (host, in a world)"); yield break; }
            CustomEvent ev = null;
            foreach (var sc in ns.scenarios) { ev = sc != null ? WorldEvents.FindEvent(sc, a[0]) : null; if (ev != null) break; }
            if (ev == null) { r.Fail($"no night event {a[0]}"); yield break; }
            var loc = StateDump.All<Location>().Where(l => l.playerBase && !l.isSubLocation)
                .OrderBy(l => StateDump.Flat(l.transform.position - p.transform.position)).FirstOrDefault();
            if (loc == null) { r.Fail("no hideout"); yield break; }
            int sent = NightObjects.SentCount;
            var was = p.whereAmI.location;
            p.whereAmI.location = loc;
            try { ev.fire(force: true); }
            finally { p.whereAmI.location = was; }
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && NightObjects.SentCount == sent) yield return null;
            yield return new WaitForSeconds(2f);
            var names = NightObjectNames();
            var made = ObjRef.Spawned.Values.Where(g => g != null && names.Contains(Name(g))).GroupBy(Name).Select(gr => $"{gr.Key} x{gr.Count()}");
            if (NightObjects.SentCount == sent) r.Fail($"{ev.name} at {loc.name} made nothing");
            r.Info = $"{ev.name} at {loc.name}: {NightObjects.SentCount - sent} objects; night objects now: {string.Join(", ", made)}";
        }

        // ---- enemy motion ----------------------------------------------------------------------------------------

        private static NetPlayerProxy ClientProxy() => NetPlayerProxy.All.Values.FirstOrDefault(x => x != null && x.Owner != Sync.PlayerSync.DummyId);

        /// <summary>"nl-herd N [KIND]" (host): N enemies (DogMutated by default) by the client's player, after it.</summary>
        private static IEnumerator Herd(string[] a, MapScenario.Result r)
        {
            int n = a.Length > 0 ? int.Parse(a[0]) : 6;
            string kind = a.Length > 1 ? a[1] : "DogMutated";
            var proxy = ClientProxy();
            if (proxy == null) { r.Fail("no client player"); yield break; }
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n;
                var spot = MapScenarioEnemies.FreeSpot(proxy.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 500f);
                var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, kind, nocturnal: false, attackPlayer: true);
                if (c == null) continue;
                c.transform.position = spot;
                _herd.Add(c);
            }
            yield return new WaitForSeconds(1f);
            if (_herd.Count == 0) { r.Fail($"couldn't spawn {kind}"); yield break; }
            r.Info = $"{_herd.Count} {kind} by the client";
        }

        private static IEnumerator Clear(string[] a, MapScenario.Result r)
        {
            int n = 0;
            foreach (var c in _herd) if (c != null) { Object.Destroy(c.gameObject); n++; }
            _herd.Clear();
            yield return new WaitForSeconds(1.5f);
            r.Info = $"removed {n}";
        }

        /// <summary>"nl-motion SECS" (client): walk round in a circle while counting how the host's enemies are drawn.</summary>
        private static IEnumerator Motion(string[] a, MapScenario.Result r)
        {
            float secs = a.Length > 0 ? float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture) : 20f;
            var p = P;
            if (p == null) { r.Fail("no player"); yield break; }
            EnemyMotion.Stats.Reset();
            EnemyMotion.Stats.On = true;
            var centre = p.transform.position;
            float until = Time.realtimeSinceStartup + secs;
            int leg = 0;
            try
            {
                while (Time.realtimeSinceStartup < until)
                {
                    float ang = leg++ * Mathf.PI / 4f;
                    var to = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 250f;
                    var walk = MapScenario.Walk(to, 60f, detours: 0);
                    float legUntil = Time.realtimeSinceStartup + 6f;
                    while (Time.realtimeSinceStartup < until && Time.realtimeSinceStartup < legUntil && walk.MoveNext()) yield return walk.Current;
                    if (p.health < p.maxHealth * 0.5f) p.health = p.maxHealth;
                }
            }
            finally { EnemyMotion.Stats.On = false; }
            p.health = p.maxHealth;
            string s = EnemyMotion.Stats.Summary();
            Plugin.Log.LogInfo($"[enemy motion] {s}");
            if (EnemyMotion.Stats.Moving == 0) r.Fail("no enemy of the host's moved by us: " + s);
            else if (EnemyMotion.Stats.Stalls * 10 > EnemyMotion.Stats.Moving) r.Fail("enemies stood still in over 10% of the frames they moved: " + s);
            r.Info = s;
        }
    }
}
