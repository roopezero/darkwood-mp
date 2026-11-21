using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
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
    internal sealed class FarEvents
    {
        private readonly CoopSession _coop;

        public FarEvents(CoopSession coop)
        {
            _coop = coop;
        }

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

        public readonly List<Entry> Log = new List<Entry>();
        /// <summary>
        /// For the map test: Location.enter and leave calls (the location
        /// really switched), by location name.
        /// </summary>
        public readonly List<Entry> Switches = new List<Entry>();

        private readonly Dictionary<Location, bool> _hostIn =
            new Dictionary<Location, bool>();
        private Location[] _all;
        private float _allAt = -100f;

        /// <summary>
        /// A joiner's exit from a location keeps its trader, porter and wolf
        /// while another player is by it (set while its exit events run).
        /// </summary>
        public bool KeepCharacters { get; private set; }

        private static bool Debug => WorldActions.Debug;
        private Session S => _coop.Net;

        internal bool Together => S.Active && WorldShare.InWorld;

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
        private FarFacts FactsOf(Location l)
        {
            bool client = S.IsClient;
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

            bool recorded = _hostIn.TryGetValue(l, out bool hostIn);
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
        internal ScriptedEvents.Part? PartFor(Location l, bool enter)
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
                _hostIn[l] = part.HostIn;
            }
            else if (part.Record == FarRecord.Forget)
            {
                _hostIn.Remove(l);
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
        internal bool Begin(
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
        internal bool Despawns(Location l)
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
        internal void Tick()
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
                    _hostIn.Remove(l);
                    continue;
                }

                if (tick == FarTick.Wait || tick == FarTick.Same)
                {
                    continue; // its enter events haven't run yet, or no change
                }

                if (tick == FarTick.Record)
                {
                    _hostIn[l] = FarEventRules.HostNear(f, false);
                    continue;
                }

                bool now = tick == FarTick.Came;
                _hostIn[l] = now;
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

        private FarTickFacts TickFactsOf()
        {
            var d = Singleton<Dreams>.Instance;
            return new FarTickFacts
            {
                Host = S.IsHost,
                InWorld = WorldShare.InWorld,
                Loading = Core.loadingGame,
                Dreaming = d != null && (d.dreaming || d.wantToDream),
                Listed = _all != null,
                ListedAt = new Seconds(_allAt),
                Now = new Seconds(Time.realtimeSinceStartup),
            };
        }

        /// <summary>
        /// Location.checkEnterEvents starts: the part its events run in.
        /// Returns whether it set ScriptedEvents.Current (the patch puts it
        /// back).
        /// </summary>
        public bool EnterEvents(Location l)
        {
            if (Core.loadingGame)
            {
                return false;
            }

            return Begin(l, enter: true, PartFor(l, enter: true));
        }

        /// <summary>
        /// Location.checkExitEvents starts: the part its events run in, and
        /// whether its characters stay (KeepCharacters, which the patch puts
        /// back). Returns whether it set ScriptedEvents.Current.
        /// </summary>
        public bool ExitEvents(Location l)
        {
            var part = PartFor(l, enter: false);
            KeepCharacters = !Despawns(l);
            string more = "";
            if (KeepCharacters)
            {
                more =
                    "; its trader, porter and wolf stay (another player is by "
                        + "it)";
            }

            return Begin(l, enter: false, part, more);
        }

        /// <summary>
        /// Location.checkExitEvents ended: KeepCharacters goes back to what
        /// it was.
        /// </summary>
        public void ExitEventsEnded(bool keep)
        {
            KeepCharacters = keep;
        }

        /// <summary>
        /// For the map test: Location.enter, when a world location really
        /// switches on.
        /// </summary>
        public void Entering(Location l, bool force)
        {
            if (Together && WorldLocation(l) && (force || !l.entered))
            {
                Note(Switches, l, "enter", ScriptedEvents.Current);
            }
        }

        /// <summary>
        /// For the map test: Location.leave, when a world location really
        /// switches off.
        /// </summary>
        public void Leaving(Location l)
        {
            if (Together && WorldLocation(l) && l.entered
                && l.gameObject.activeInHierarchy)
            {
                Note(Switches, l, "leave", ScriptedEvents.Current);
            }
        }
    }
}
