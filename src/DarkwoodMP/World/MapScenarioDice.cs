using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the determinism audit (docs/MULTIPLAYER_DETERMINISM_AUDIT.md): things each game rolls with
    /// its own random numbers that could make two games' worlds differ. The list is tools/tests/map_scenario_dice.py.
    ///
    ///   dice-sets    the set of children each Location kept (Location.Awake picks one of Location.sets at random,
    ///                every time the location is made, and destroys the others; the pick isn't saved). Replies with
    ///                the count and a digest, and logs every pick, so the host's and the client's can be compared.
    ///   dice-events  the scripted-event steps in the loaded world whose target is a random object of their location
    ///                (Location.GetObject.returnObjects without getAll or getClosest) or the object nearest the local
    ///                player (getClosestToPlayer), in a step every game runs (the shared part, ScriptedEvents), and
    ///                with more than one object to pick from. Also the runFunction steps of the shared part.
    /// </summary>
    public static class MapScenarioDice
    {
        /// <summary>Each Location that had sets: "name@x,z" -> the set it kept, and of how many.</summary>
        internal static readonly Dictionary<string, string> Picks = new Dictionary<string, string>();

        public static void Register()
        {
            Add("dice-sets", (a, r) =>
            {
                var lines = Picks.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}").ToList();
                foreach (var l in lines) Plugin.Log.LogInfo($"[dice] set {l}");
                uint h = 2166136261;
                foreach (char c in string.Join("|", lines)) { h ^= c; h *= 16777619; }
                r.Info = $"{lines.Count} locations with sets, digest {h:x8}";
                return Nothing();
            });
            Add("dice-events", (a, r) =>
            {
                int steps = 0, random = 0, nearest = 0, functions = 0;
                var names = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var ge in Resources.FindObjectsOfTypeAll<GameEvents>())
                {
                    if (ge == null || !ge.gameObject.scene.IsValid() || ge.events == null) continue;
                    Location loc = null;
                    try { loc = ge.transform.GetLocation(); } catch { }
                    for (int i = 0; i < ge.events.Count; i++)
                    {
                        var e = ge.events[i];
                        if (e == null || ScriptedEvents.IsPlayerStep(e.type) || ScriptedEvents.IsHostStep(e.type)) continue;
                        steps++;
                        if (e.type == GameEvent.Type.runFunction && !string.IsNullOrEmpty(e.Value))
                        {
                            functions++;
                            names.TryGetValue(e.Value, out int n); names[e.Value] = n + 1;
                        }
                        var gets = new List<Location.GetObject>();
                        if (e.locationObjects != null) gets.AddRange(e.locationObjects.Where(g => g != null));
                        if (e.getTargetFromLocation && e.targetLocationObject != null) gets.Add(e.targetLocationObject);
                        foreach (var g in gets)
                        {
                            if (g.getAll || g.getClosest) continue;
                            int count = -1;
                            if (loc != null) { try { count = loc.getObjects(g)?.Count ?? 0; } catch { } }
                            if (count >= 0 && count < 2) continue;
                            if (g.getClosestToPlayer) nearest++; else random++;
                            Plugin.Log.LogInfo($"[dice] event {ge.name} step {i} {e.type} picks a {(g.getClosestToPlayer ? "nearest-to-own-player" : "random")} {g.type} of {(count < 0 ? "?" : count.ToString())} in {(loc != null ? loc.name : "no location")}");
                        }
                    }
                }
                foreach (var n in names) Plugin.Log.LogInfo($"[dice] runFunction {n.Key} x{n.Value}");
                // Things that explode in every game (BreakSync's Explode) and spawn objects at random spots round
                // them: only thrown ones' spawns are the host's (ObjectActions.ThrownLeavesPatch).
                var blasts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var x in Resources.FindObjectsOfTypeAll<Explodes>())
                {
                    if (x == null || x.spawnObject == null || x.objectAmount <= 0 || x.GetComponent<ThrownItem>() != null) continue;
                    string k = $"{x.name.Replace("(Clone)", "")}->{x.spawnObject.name} x{x.objectAmount}{(x.gameObject.scene.IsValid() ? "" : " (prefab)")}";
                    blasts.TryGetValue(k, out int n); blasts[k] = n + 1;
                }
                foreach (var b in blasts) Plugin.Log.LogInfo($"[dice] explodes {b.Key}: {b.Value}");
                r.Info = $"{steps} shared steps; {random} pick a random object, {nearest} the one nearest the own player; {functions} runFunction ({string.Join(",", names.Keys)}); {blasts.Count} kinds of unthrown spawning explosions";
                return Nothing();
            });
        }

        private static IEnumerator Nothing() { yield break; }
    }

    /// <summary>Notes which of its sets a Location kept (Location.Awake), for dice-sets.</summary>
    [HarmonyPatch(typeof(Location), "Awake")]
    internal static class LocationSetsPatch
    {
        private static void Prefix(Location __instance, out List<GameObject> __state)
        {
            __state = __instance != null && __instance.sets != null && __instance.sets.Count > 1 ? new List<GameObject>(__instance.sets) : null;
        }

        private static void Postfix(Location __instance, List<GameObject> __state)
        {
            if (__state == null || __instance == null) return;
            var kept = __state.FirstOrDefault(g => g != null && !__instance.sets.Contains(g));
            var p = __instance.transform.position;
            MapScenarioDice.Picks[FormattableString.Invariant($"{__instance.name}@{p.x:0},{p.z:0}")] = $"{(kept != null ? kept.name : "?")} of {__state.Count}";
        }
    }
}
