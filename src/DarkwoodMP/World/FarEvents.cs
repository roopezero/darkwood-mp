using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A world location's enter and exit events (EventTriggers of type onEnterLocation and onExitLocation, which
    /// Location.OnActivated and Location.leave set off as culling enters or leaves the location) with players far
    /// apart.
    ///
    /// The host's culling keeps every player's area entered (Combat.SharedCullingPatch), so a location near a joiner
    /// is entered in the host's game even when the host's player is nowhere near it. Its events are then run on the
    /// host as the host's and the shared part only (ScriptedEvents.Part.HostAndShared): the world's side of them
    /// (characters spawned, flags, objects shown or hidden) happens, but the host's player gets no message, dialogue,
    /// item or trip to a place it isn't at. Its events run in full only when the host's player is in the location's
    /// cull rect as it is entered. When the host's player comes into a location the world already has entered for
    /// someone else, or goes out of one that stays entered for them, only its own part of the events runs
    /// (Part.PlayerOnly). The world's exit (despawning the trader, the porter and the wolf) happens when the
    /// location is left, which on the host is when the last player has gone.
    ///
    /// A joiner's game enters locations around its own player only, so its events there are its player's: they run
    /// as its own and the shared part (Part.PlayerAndShared), not the host's (doors and switches are the host's, and
    /// the host runs them in its own game). Its exit despawns nothing while another player is still by the location.
    /// </summary>
    public static class FarEvents
    {
        /// <summary>For the map test: each enter or exit, with the part it ran in.</summary>
        public sealed class Entry { public float At; public string Location; public string Kind; public ScriptedEvents.Part Part; public Vector3 Pos; }
        public static readonly List<Entry> Log = new List<Entry>();
        /// <summary>For the map test: Location.enter and leave calls (the location really switched), by location name.</summary>
        public static readonly List<Entry> Switches = new List<Entry>();

        private static readonly Dictionary<Location, bool> HostIn = new Dictionary<Location, bool>();
        private static Location[] _all;
        private static float _allAt = -100f;

        private static bool Debug => WorldActions.Debug;
        private static Session S => Session.Instance;
        internal static bool Together => S != null && S.Active && WorldShare.InWorld;

        internal static bool WorldLocation(Location l) => l != null && !l.isSubLocation && !l.isOutsideLocation;

        internal static void Note(List<Entry> list, Location l, string kind, ScriptedEvents.Part part)
        {
            if (list.Count > 2000) list.RemoveRange(0, 1000);
            list.Add(new Entry { At = Time.realtimeSinceStartup, Location = Core.getTrueLocationName(l.name), Kind = kind, Part = part, Pos = l.transform.position });
        }

        /// <summary>Host: whether our own player is by this location (in its cull rect; `was`: by it last time, a margin to leave it).</summary>
        internal static bool HostNear(Location l, bool was)
        {
            var p = Player.Instance;
            if (p == null || SharedCullingPatch.HostAway) return false;
            SharedCullingPatch.Distances(out int dx, out int dy);
            float m = was ? 600f : 0f;
            var a = p.transform.position; var b = l.transform.position;
            return Mathf.Abs(a.x - b.x) <= dx + m && Mathf.Abs(a.z - b.z) <= dy + m;
        }

        /// <summary>Client: whether another player (not in a separate location or a dream) is by this location.</summary>
        private static bool OtherNear(Location l)
        {
            var sync = Sync.PlayerSync.Instance;
            if (sync == null) return false;
            SharedCullingPatch.Distances(out int dx, out int dy); // theirs isn't known here: about ours
            var b = l.transform.position;
            foreach (var r in sync.Remotes.Values)
            {
                if (r == null || r.Root == null || r.Away || r.Owner == Sync.PlayerSync.DummyId) continue;
                var a = r.Root.transform.position;
                if (Mathf.Abs(a.x - b.x) <= dx && Mathf.Abs(a.z - b.z) <= dy) return true;
            }
            return false;
        }

        /// <summary>The part a location's enter (or exit) events run in now; null: as the game runs them.</summary>
        internal static ScriptedEvents.Part? PartFor(Location l, bool enter)
        {
            if (!Together || !WorldLocation(l) || ScriptedEvents.Current != ScriptedEvents.Part.All || WorldActions.Replaying) return null;
            if (S.IsClient) return ScriptedEvents.Part.PlayerAndShared;
            bool hostIn;
            if (enter) HostIn[l] = hostIn = !Proxies.Any || HostNear(l, true); // with the margin: the location's node is up to 300 nearer than it
            else { if (!HostIn.TryGetValue(l, out hostIn)) hostIn = true; HostIn.Remove(l); }
            return hostIn ? (ScriptedEvents.Part?)null : ScriptedEvents.Part.HostAndShared;
        }

        /// <summary>Whether the location's exit despawns its trader, porter and wolf here.</summary>
        internal static bool Despawns(Location l) => !(Together && S.IsClient && WorldLocation(l) && OtherNear(l));

        /// <summary>
        /// Host, each culling refresh: our player came into a location already entered for someone else, or went out
        /// of one that stays entered: its own part of the location's enter or exit events.
        /// </summary>
        internal static void Tick()
        {
            var s = S;
            if (s == null || !s.IsHost || !WorldShare.InWorld || Core.loadingGame) return;
            var d = Singleton<Dreams>.Instance;
            if (d != null && (d.dreaming || d.wantToDream)) return;
            if (_all == null || Time.realtimeSinceStartup - _allAt > 10f)
            {
                _all = Object.FindObjectsOfType<Location>(true).Where(WorldLocation).ToArray();
                _allAt = Time.realtimeSinceStartup;
            }
            foreach (var l in _all)
            {
                if (l == null) continue;
                if (!l.entered) { HostIn.Remove(l); continue; }
                if (!l.finishedLoading || l.activatingOverTime) continue; // its enter events haven't run yet
                bool was;
                if (!HostIn.TryGetValue(l, out was)) { HostIn[l] = HostNear(l, false); continue; }
                bool now = HostNear(l, was);
                if (now == was) continue;
                HostIn[l] = now;
                var type = now ? global::EventTrigger.Type.onEnterLocation : global::EventTrigger.Type.onExitLocation;
                Note(Log, l, now ? "enter" : "exit", ScriptedEvents.Part.PlayerOnly);
                if (Debug) Plugin.Log.LogInfo($"[farevents] our player {(now ? "came into" : "went out of")} {l.name}, kept entered for the others: its {(now ? "enter" : "exit")} events, our player's part only");
                ScriptedEvents.RunAs(ScriptedEvents.Part.PlayerOnly, replaying: false, () =>
                {
                    foreach (var et in l.events)
                        if (et != null)
                            try { Core.sendTriggerInfo(et.gameObject, type); }
                            catch (Exception e) { Plugin.Log.LogWarning($"[farevents] {l.name}'s {type} on {et.name}: {e.Message}"); }
                });
            }
        }

        public static void Register(Session s) => s.OnSessionEnded += () => { HostIn.Clear(); _all = null; };
    }

    [HarmonyPatch(typeof(Location), "checkEnterEvents")]
    static class LocationEnterEventsPatch
    {
        static void Prefix(Location __instance, out (bool, ScriptedEvents.Part) __state)
        {
            __state = (false, ScriptedEvents.Current);
            if (Core.loadingGame) return;
            var part = FarEvents.PartFor(__instance, enter: true);
            if (FarEvents.Together && FarEvents.WorldLocation(__instance))
            {
                FarEvents.Note(FarEvents.Log, __instance, "enter", part ?? ScriptedEvents.Part.All);
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[farevents] {__instance.name} entered: its enter events run as {part ?? ScriptedEvents.Part.All}");
            }
            if (part == null) return;
            __state.Item1 = true;
            ScriptedEvents.Current = part.Value;
        }

        static Exception Finalizer(Exception __exception, (bool, ScriptedEvents.Part) __state)
        {
            if (__state.Item1) ScriptedEvents.Current = __state.Item2;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Location), "checkExitEvents")]
    static class LocationExitEventsPatch
    {
        internal static bool KeepCharacters;

        static void Prefix(Location __instance, out (bool, ScriptedEvents.Part, bool) __state)
        {
            __state = (false, ScriptedEvents.Current, KeepCharacters);
            var part = FarEvents.PartFor(__instance, enter: false);
            KeepCharacters = !FarEvents.Despawns(__instance);
            if (FarEvents.Together && FarEvents.WorldLocation(__instance))
            {
                FarEvents.Note(FarEvents.Log, __instance, "exit", part ?? ScriptedEvents.Part.All);
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[farevents] {__instance.name} left: its exit events run as {part ?? ScriptedEvents.Part.All}" +
                                                           (KeepCharacters ? "; its trader, porter and wolf stay (another player is by it)" : ""));
            }
            if (part == null) return;
            __state.Item1 = true;
            ScriptedEvents.Current = part.Value;
        }

        static Exception Finalizer(Exception __exception, (bool, ScriptedEvents.Part, bool) __state)
        {
            if (__state.Item1) ScriptedEvents.Current = __state.Item2;
            KeepCharacters = __state.Item3;
            return __exception;
        }
    }

    /// <summary>A joiner's exit from a location doesn't take away its trader, porter or wolf while another player is by it.</summary>
    [HarmonyPatch(typeof(Location))]
    static class LocationExitDespawnPatch
    {
        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnTrader))] static bool Trader() => !LocationExitEventsPatch.KeepCharacters;
        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnPorter))] static bool Porter() => !LocationExitEventsPatch.KeepCharacters;
        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnWolf))] static bool Wolf() => !LocationExitEventsPatch.KeepCharacters;
    }

    /// <summary>For the map test: when a world location really switches on or off (culling entered or left it).</summary>
    [HarmonyPatch(typeof(Location))]
    static class LocationSwitchCountPatch
    {
        [HarmonyPrefix, HarmonyPatch(nameof(Location.enter))]
        static void Enter(Location __instance, bool force)
        {
            if (FarEvents.Together && FarEvents.WorldLocation(__instance) && (force || !__instance.entered))
                FarEvents.Note(FarEvents.Switches, __instance, "enter", ScriptedEvents.Current);
        }

        [HarmonyPrefix, HarmonyPatch(nameof(Location.leave))]
        static void Leave(Location __instance, bool force)
        {
            if (FarEvents.Together && FarEvents.WorldLocation(__instance) && __instance.entered && __instance.gameObject.activeInHierarchy)
                FarEvents.Note(FarEvents.Switches, __instance, "leave", ScriptedEvents.Current);
        }
    }
}
