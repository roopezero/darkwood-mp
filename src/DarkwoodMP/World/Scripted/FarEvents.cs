using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World.Scripted
{
    /// <summary>
    /// A world location's enter and exit events (EventTriggers of type
    /// onEnterLocation and onExitLocation, which Location.OnActivated and
    /// Location.leave set off as culling enters or leaves the location) with
    /// players far apart.
    ///
    /// The host's culling keeps every player's area entered
    /// (Combat.SharedCullingPatch), so a location near a joiner is entered in
    /// the host's game even when the host's player is nowhere near it. Its
    /// events are then run on the host as the host's and the shared part only
    /// (ScriptedEvents.Part.HostAndShared): the world's side of them
    /// (characters spawned, flags, objects shown or hidden) happens, but the
    /// host's player gets no message, dialogue, item or trip to a place it
    /// isn't at. Its events run in full only when the host's player is in the
    /// location's cull rect as it is entered. When the host's player comes into
    /// a location the world already has entered for someone else, or goes out
    /// of one that stays entered for them, only its own part of the events runs
    /// (Part.PlayerOnly). The world's exit (despawning the trader, the porter
    /// and the wolf) happens when the location is left, which on the host is
    /// when the last player has gone.
    ///
    /// A joiner's game enters locations around its own player only, so its
    /// events there are its player's: they run as its own and the shared part
    /// (Part.PlayerAndShared), not the host's (doors and switches are the
    /// host's, and the host runs them in its own game). Its exit despawns
    /// nothing while another player is still by the location.
    /// </summary>
    public static class FarEvents
    {
        /// <summary>
        /// For the map test: each enter or exit, with the part it ran in.
        /// </summary>
        public sealed class Entry
        {
            public float At;
            public string Location;
            public string Kind;
            public ScriptedEvents.Part Part;
            public Vector3 Pos;
        }

        public static readonly List<Entry> Log = new List<Entry>();
        /// <summary>
        /// For the map test: Location.enter and leave calls (the location
        /// really switched), by location name.
        /// </summary>
        public static readonly List<Entry> Switches = new List<Entry>();

        private static readonly Dictionary<Location, bool> HostIn =
            new Dictionary<Location, bool>();
        private static Location[] _all;
        private static float _allAt = -100f;

        private static bool Debug => WorldActions.Debug;
        private static Session S => Session.Instance;
        internal static bool Together =>
            S != null && S.Active && WorldShare.InWorld;

        internal static bool WorldLocation(Location l) =>
            l != null
                && FarEventRules.IsWorldLocation(
                    l.isSubLocation, l.isOutsideLocation);

        internal static void Note(
            List<Entry> list, Location l, string kind, ScriptedEvents.Part part)
        {
            if (list.Count > 2000)
            {
                list.RemoveRange(0, 1000);
            }

            list.Add(new Entry
            {
                At = Time.realtimeSinceStartup,
                Location = Core.getTrueLocationName(l.name),
                Kind = kind,
                Part = part,
                Pos = l.transform.position
            });
        }

        /// <summary>
        /// The facts of a location for FarEventRules: where it and the host's
        /// player are, our culling's distances, and the host's record of it.
        /// </summary>
        private static FarFacts FactsOf(Location l)
        {
            var s = S;
            bool client = s != null && s.IsClient;
            SharedCullingPatch.Distances(out int dx, out int dy);
            var b = l.transform.position;
            var p = Player.Instance;
            var host = new HostSpot { Known = p != null };
            if (p != null)
            {
                var a = p.transform.position;
                host = new HostSpot
                {
                    Known = true,
                    Away = SharedCullingPatch.HostAway,
                    Pos = new Vec2(a.x, a.z),
                };
            }

            bool recorded = HostIn.TryGetValue(l, out bool hostIn);
            return new FarFacts
            {
                Together = Together,
                Sub = l.isSubLocation,
                Outside = l.isOutsideLocation,
                Current = (EventPart)ScriptedEvents.Current,
                Replaying = Coop.Guards.Replaying,
                Client = client,
                AnyProxies = Proxies.Any,
                Host = host,
                CullX = dx,
                CullZ = dy,
                At = new Vec2(b.x, b.z),
                Recorded = recorded,
                HostIn = hostIn,
                Entered = l.entered,
                Loaded = l.finishedLoading,
                Activating = l.activatingOverTime,
            };
        }

        /// <summary>
        /// Client: the other players' puppets, for whether one is by a
        /// location.
        /// </summary>
        private static List<RemoteSpot> RemoteSpots()
        {
            var spots = new List<RemoteSpot>();
            var sync = Plugin.Instance.Host.Players;
            if (sync == null)
            {
                return spots;
            }

            foreach (var r in sync.Remotes.Values)
            {
                if (r == null)
                {
                    continue;
                }

                bool shown = r.Root != null;
                Vec2 pos = default;
                if (shown)
                {
                    var a = r.Root.transform.position;
                    pos = new Vec2(a.x, a.z);
                }

                spots.Add(new RemoteSpot
                {
                    Shown = shown,
                    Away = r.Away,
                    Dummy = r.Owner == Sync.PlayerSync.DummyId,
                    Pos = pos,
                });
            }

            return spots;
        }

        /// <summary>
        /// The part a location's enter (or exit) events run in now; null: as
        /// the game runs them.
        /// </summary>
        internal static ScriptedEvents.Part? PartFor(Location l, bool enter)
        {
            // not together: as the game runs them (FarEventRules.OnEnter)
            if (l == null || !Together)
            {
                return null;
            }

            FarFacts f = FactsOf(l);
            FarPart part = FarEventRules.OnExit(f);
            if (enter)
            {
                part = FarEventRules.OnEnter(f);
            }

            if (part.Record == FarRecord.Store)
            {
                HostIn[l] = part.HostIn;
            }
            else if (part.Record == FarRecord.Forget)
            {
                HostIn.Remove(l);
            }

            if (!part.Set)
            {
                return null;
            }

            return (ScriptedEvents.Part)part.Part;
        }

        /// <summary>
        /// A location's enter or exit events start: noted and logged (for a
        /// world location while together), and ScriptedEvents.Current set to
        /// `part`. Returns whether it set it (the caller's Finalizer puts it
        /// back).
        /// </summary>
        internal static bool Begin(
            Location l, bool enter, ScriptedEvents.Part? part, string more = "")
        {
            if (Together && WorldLocation(l))
            {
                ScriptedEvents.Part ran;
                if (part.HasValue)
                {
                    ran = part.Value;
                }
                else
                {
                    ran = ScriptedEvents.Part.All;
                }

                string kind = "exit";
                if (enter)
                {
                    kind = "enter";
                }

                Note(Log, l, kind, ran);
                if (Debug)
                {
                    string text = "left: its exit";
                    if (enter)
                    {
                        text = "entered: its enter";
                    }

                    Plugin.Log.LogInfo(
                        $"[farevents] {l.name} {text} events run as "
                            + $"{ran}{more}");
                }
            }

            if (part == null)
            {
                return false;
            }

            ScriptedEvents.Current = part.Value;
            return true;
        }

        /// <summary>
        /// Whether the location's exit despawns its trader, porter and wolf
        /// here.
        /// </summary>
        internal static bool Despawns(Location l)
        {
            // not together: it despawns (FarEventRules.Despawns)
            if (l == null || !Together)
            {
                return true;
            }

            FarFacts f = FactsOf(l);
            var remotes = new List<RemoteSpot>();
            if (f.Together && f.Client)
            {
                remotes = RemoteSpots();
            }

            return FarEventRules.Despawns(f, remotes);
        }

        /// <summary>
        /// Host, each culling refresh: our player came into a location already
        /// entered for someone else, or went out of one that stays entered: its
        /// own part of the location's enter or exit events.
        /// </summary>
        internal static void Tick()
        {
            if (!FarEventRules.MayTick(TickFactsOf()))
            {
                return;
            }

            if (FarEventRules.Relist(TickFactsOf()))
            {
                _all = Object.FindObjectsOfType<Location>(true)
                    .Where(WorldLocation).ToArray();
                _allAt = Time.realtimeSinceStartup;
            }

            foreach (var l in _all)
            {
                if (l == null)
                {
                    continue;
                }

                FarFacts f = FactsOf(l);
                FarTick tick = FarEventRules.Tick(f);
                if (tick == FarTick.Forget)
                {
                    HostIn.Remove(l);
                    continue;
                }

                if (tick == FarTick.Wait || tick == FarTick.Same)
                {
                    continue; // its enter events haven't run yet, or no change
                }

                if (tick == FarTick.Record)
                {
                    HostIn[l] = FarEventRules.HostNear(f, false);
                    continue;
                }

                bool now = tick == FarTick.Came;
                HostIn[l] = now;
                global::EventTrigger.Type type;
                if (now)
                {
                    type = global::EventTrigger.Type.onEnterLocation;
                }
                else
                {
                    type = global::EventTrigger.Type.onExitLocation;
                }

                string kind = "exit";
                if (now)
                {
                    kind = "enter";
                }

                Note(Log, l, kind, ScriptedEvents.Part.PlayerOnly);
                if (Debug)
                {
                    string text = "went out of";
                    if (now)
                    {
                        text = "came into";
                    }

                    string which = "exit";
                    if (now)
                    {
                        which = "enter";
                    }

                    Plugin.Log.LogInfo(
                        $"[farevents] our player {text} {l.name}, kept entered "
                            + $"for the others: its {which} events, our "
                            + "player's part only");
                }

                ScriptedEvents.RunAs(ScriptedEvents.Part.PlayerOnly,
                    replaying: false, () =>
                {
                    foreach (var et in l.events)
                    {
                        if (et != null)
                        {
                            try
                            {
                                Core.sendTriggerInfo(et.gameObject, type);
                            }
                            catch (Exception e)
                            {
                                Plugin.Log.LogWarning(
                                    $"[farevents] {l.name}'s {type} on "
                                        + $"{et.name}: {e.Message}");
                            }
                        }
                    }
                });
            }
        }

        private static FarTickFacts TickFactsOf()
        {
            var s = S;
            var d = Singleton<Dreams>.Instance;
            return new FarTickFacts
            {
                Host = s != null && s.IsHost,
                InWorld = WorldShare.InWorld,
                Loading = Core.loadingGame,
                Dreaming = d != null && (d.dreaming || d.wantToDream),
                Listed = _all != null,
                ListedAt = new Seconds(_allAt),
                Now = new Seconds(Time.realtimeSinceStartup),
            };
        }

        public static void Register(Session s) => s.OnSessionEnded += () =>
        {
            HostIn.Clear();
            _all = null;
        };
    }

    [HarmonyPatch(typeof(Location), "checkEnterEvents")]
    static class LocationEnterEventsPatch
    {
        static void Prefix(
            Location __instance, out (bool, ScriptedEvents.Part) __state)
        {
            __state = (false, ScriptedEvents.Current);
            if (Core.loadingGame)
            {
                return;
            }

            __state.Item1 = FarEvents.Begin(
                __instance, enter: true,
                FarEvents.PartFor(__instance, enter: true));
        }

        static Exception Finalizer(
            Exception __exception, (bool, ScriptedEvents.Part) __state)
        {
            if (__state.Item1)
            {
                ScriptedEvents.Current = __state.Item2;
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Location), "checkExitEvents")]
    static class LocationExitEventsPatch
    {
        internal static bool KeepCharacters;

        static void Prefix(
            Location __instance, out (bool, ScriptedEvents.Part, bool) __state)
        {
            __state = (false, ScriptedEvents.Current, KeepCharacters);
            var part = FarEvents.PartFor(__instance, enter: false);
            KeepCharacters = !FarEvents.Despawns(__instance);
            string more = "";
            if (KeepCharacters)
            {
                more =
                    "; its trader, porter and wolf stay (another player is by "
                        + "it)";
            }

            __state.Item1 = FarEvents.Begin(__instance, enter: false, part,
                more);
        }

        static Exception Finalizer(
            Exception __exception, (bool, ScriptedEvents.Part, bool) __state)
        {
            if (__state.Item1)
            {
                ScriptedEvents.Current = __state.Item2;
            }

            KeepCharacters = __state.Item3;
            return __exception;
        }
    }

    /// <summary>
    /// A joiner's exit from a location doesn't take away its trader, porter or
    /// wolf while another player is by it.
    /// </summary>
    [HarmonyPatch(typeof(Location))]
    static class LocationExitDespawnPatch
    {
        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnTrader))]
        static bool Trader() =>
            !LocationExitEventsPatch.KeepCharacters;
        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnPorter))]
        static bool Porter() =>
            !LocationExitEventsPatch.KeepCharacters;
        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnWolf))]
        static bool Wolf() =>
            !LocationExitEventsPatch.KeepCharacters;
    }

    /// <summary>
    /// For the map test: when a world location really switches on or off
    /// (culling entered or left it).
    /// </summary>
    [HarmonyPatch(typeof(Location))]
    static class LocationSwitchCountPatch
    {
        [HarmonyPrefix, HarmonyPatch(nameof(Location.enter))]
        static void Enter(Location __instance, bool force)
        {
            if (FarEvents.Together && FarEvents.WorldLocation(__instance)
                && (force || !__instance.entered))
            {
                FarEvents.Note(
                    FarEvents.Switches, __instance, "enter",
                    ScriptedEvents.Current);
            }
        }

        [HarmonyPrefix, HarmonyPatch(nameof(Location.leave))]
        static void Leave(Location __instance, bool force)
        {
            if (FarEvents.Together && FarEvents.WorldLocation(__instance)
                && __instance.entered
                && __instance.gameObject.activeInHierarchy)
            {
                FarEvents.Note(
                    FarEvents.Switches, __instance, "leave",
                    ScriptedEvents.Current);
            }
        }
    }
}
