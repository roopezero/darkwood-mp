using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DarkwoodMP.Net;
using DarkwoodMP.World.Places;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Separate locations (caves, interiors) and dreams stand in the same place
    /// in every game.
    ///
    /// The game loads a location the first time a player goes there
    /// (OutsideLocations.spawnLocation): its marker goes to the next free one
    /// of a list of far-off spots
    /// (locationPositions[actualSpawnedLocationsCount]) and the location is
    /// turned by Core.getRandomHalfRotation (LocationMarker.spawnLocation). So
    /// each game would put a location first visited after joining wherever its
    /// own visiting order and dice put it. Here the host decides:
    /// a joiner about to load a location it hasn't got asks the host
    /// (LocationAsk) and loads it where the host says (LocationSlot: the spot
    /// and the turn). A location the host has loaded is where it is in the
    /// host's game. One the host hasn't got is given the host's next free spot
    /// and a turn, and (unless it is a dream) the host loads it too, without
    /// going there, so the host can judge the joiner's looting and doors there
    /// like the world's, and the location ends up in the host's save. The
    /// host's own later visit loads it where it promised.
    ///
    /// Players are in the same place when both are in the shared world, or both
    /// in the same location (by name). Dreams are each player's own, except the
    /// shared level dream (World.DreamSync): players in it see each other
    /// there.
    /// </summary>
    public static class Locations
    {
        private static Session S => Session.Instance;

        /// <summary>
        /// Where each location stands. Host: what it promised or used; client:
        /// what the host said, the names asked for, and the names given up on
        /// (loaded where our own game puts them).
        /// </summary>
        private static readonly LocationSpots _spots = new LocationSpots();
        /// <summary>
        /// Host: locations promised to a joiner, to load in our game when it
        /// isn't busy.
        /// </summary>
        private static readonly Queue<string> _toSpawn = new Queue<string>();
        private static string _spawning;
        private static string _gridBefore;
        private static float _spawningSince;
        private static LocationNames _real;

        /// <summary>
        /// Host: how many locations we loaded for joiners (for the tests).
        /// </summary>
        public static int LoadedForOthers;
        /// <summary>
        /// Host: a location promised to a joiner is still to load.
        /// </summary>
        public static bool Busy => _spawning != null || _toSpawn.Count > 0;

        private static readonly
            AccessTools.FieldRef<OutsideLocations, List<Vector3>> PositionsOf =
                AccessTools.FieldRefAccess<OutsideLocations, List<Vector3>>(
                    "locationPositions");

        public static void Register(Session s)
        {
            s.Register(MsgType.LocationAsk, OnAsk);
            s.Register(MsgType.LocationSlot, OnSlot);
            s.OnSessionEnded += () =>
            {
                _spots.Clear();
                _toSpawn.Clear();
                _spawning = null;
                _kept.Clear();
                _quietEnter.Clear();
            };
        }

        // ---- host: running a location for the joiners in it -----------------

        /// <summary>
        /// Host: separate locations kept entered (running) because a joiner is
        /// in them.
        /// </summary>
        private static readonly HashSet<Location> _kept =
            new HashSet<Location>();
        /// <summary>
        /// Host: locations we entered for a joiner: their enter events are the
        /// joiner's game's to run, not ours.
        /// </summary>
        private static readonly HashSet<Location> _quietEnter =
            new HashSet<Location>();
        private static float _nextKeep;

        /// <summary>
        /// Host: whether we keep this location running for a joiner in it (our
        /// player may be elsewhere).
        /// </summary>
        public static bool KeptForOthers(Location l) =>
            l != null && _kept.Contains(l);

        /// <summary>
        /// Host: whether our own player is in this separate location now.
        /// </summary>
        public static bool OursNow(Location l)
        {
            var o = Singleton<OutsideLocations>.Instance;
            bool inside = l != null && o != null && o.playerInOutsideLocation;
            string ours = null;
            string theirs = null;
            if (inside)
            {
                string current = o.currentLocationName;
                if (current == null)
                {
                    current = "";
                }

                ours = Core.getTrueLocationName(current);
                theirs = Core.getTrueLocationName(l.name);
            }

            return PlaceRules.OursNow(inside, ours, theirs);
        }

        /// <summary>
        /// Host: the separate location a joiner who says they're at `place`
        /// (their PlayerState) and stands at pos is in, when we have it at that
        /// spot; the shared level dream when our player dreams it too
        /// (SharedDream); null for the world, any other dream (each player's
        /// own), or a location we haven't got (they loaded it before the host
        /// answered: it stands somewhere else here).
        /// </summary>
        public static Location HostRunsAt(string place, Vector3 pos)
        {
            PlaceKind kind = PlaceRules.Kind(place);
            if (kind == PlaceKind.None)
            {
                return null;
            }

            Location loc = null;
            if (kind == PlaceKind.Dream)
            {
                // The shared level dream, when our player dreams it too: it is
                // ours to run, as a location is.
                loc = SharedDream();
                if (!PlaceRules.RunsDream(loc != null, place, Current()))
                {
                    return null;
                }
            }
            else
            {
                loc = LoadedAt(place);
            }

            if (loc == null)
            {
                return null;
            }

            if (PlaceRules.InReach(VecOf(pos), VecOf(loc.transform.position)))
            {
                return loc;
            }

            return null;
        }

        /// <summary>
        /// Host: the loaded location a joiner's place names, by its key in
        /// spawnedLocations or its true name.
        /// </summary>
        private static Location LoadedAt(string place)
        {
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null)
            {
                return null;
            }

            foreach (var kv in o.spawnedLocations)
            {
                if (kv.Value != null
                    && PlaceRules.Matches(
                        kv.Key, Core.getTrueLocationName(kv.Key), place))
                {
                    return kv.Value;
                }
            }

            return null;
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// Kept locations whose loose objects DragSync has noted (once each
        /// time it starts being kept).
        /// </summary>
        private static readonly HashSet<Location> _noted =
            new HashSet<Location>();

        /// <summary>
        /// Host, twice a second: the locations joiners are in are entered
        /// (their objects and characters switched on, as the game does when its
        /// player goes there) even when our player isn't there, so our game
        /// runs their enemies and NetEnemies sends them to those joiners. One
        /// nobody is in any more is left again, unless our player is in it.
        /// </summary>
        private static void KeepRunning(OutsideLocations o)
        {
            var now = new Seconds(Time.unscaledTime);
            if (!HostLocationRules.KeepDue(now, new Seconds(_nextKeep)))
            {
                return;
            }

            _nextKeep = HostLocationRules.NextKeep(now).Value;
            var want = new HashSet<Location>();
            Dreams dreams = Singleton<Dreams>.Instance;
            Location dream = null;
            if (dreams != null)
            {
                dream = dreams.dreamLocation;
            }

            foreach (var p in Combat.NetPlayerProxy.All.Values)
            {
                // the shared dream runs with our player in it
                Location there = null;
                if (p != null)
                {
                    there = p.InLocation;
                }

                if (HostLocationRules.Wanted(
                    p != null, there != null, there == dream))
                {
                    want.Add(there);
                }
            }

            foreach (var loc in want)
            {
                if (_kept.Add(loc))
                {
                    string place = Core.getTrueLocationName(loc.name);
                    string note = "";
                    if (loc.entered)
                    {
                        note = " (already entered)";
                    }

                    Plugin.Log.LogInfo(
                        $"[locations] keeping {place} running for the "
                            + $"joiner(s) in it{note}");
                }

                EnterStep step = HostLocationRules.EnterFor(
                    loc.entered, loc.gameObject.activeInHierarchy);
                if (step == EnterStep.ForceEnter)
                {
                    _quietEnter.Add(loc);
                    loc.enter(force: true);
                }
                else if (step == EnterStep.Enter)
                {
                    _quietEnter.Add(loc);
                    loc.enter(force: false);
                }

                if (_noted.Add(loc))
                {
                    CoopSession coop = Coop.Current;
                    if (coop != null)
                    {
                        coop.Drags.NoteAll(loc.gameObject);
                    }
                }
            }

            _kept.RemoveWhere(l => l == null);
            _noted.RemoveWhere(l => l == null || !_kept.Contains(l));
            foreach (var loc in _kept.Where(l => !want.Contains(l)).ToList())
            {
                _kept.Remove(loc);
                _quietEnter.Remove(loc);
                string name = Core.getTrueLocationName(loc.name);
                bool ours = OursNow(loc);
                string text = ": left";
                if (ours)
                {
                    text = "; our player is, so it stays";
                }

                Plugin.Log.LogInfo(
                    $"[locations] nobody else is in {name} any more{text}");
                if (!HostLocationRules.LeavesUnwanted(ours))
                {
                    continue;
                }

                loc.leave(force: true);
                WorldGrid worldGrid = Singleton<WorldGrid>.Instance;
                if (worldGrid != null)
                {
                    WorldGrid.Grid grid = worldGrid.getGrid(name);
                    if (grid != null)
                    {
                        grid.leave();
                    }
                }
            }
        }

        /// <summary>
        /// Host: Location.leave on a location kept for a joiner (our player
        /// going back to the world, or on to another location:
        /// OutsideLocations.leaveAllLocations) doesn't switch it off. Our
        /// player's exit events still run.
        /// </summary>
        internal static bool BeforeLeave(Location loc, bool force)
        {
            LeaveStep step = HostLocationRules.BeforeLeave(LeaveOf(loc, force));
            if (step == LeaveStep.Leave)
            {
                _quietEnter.Remove(loc);
                return true;
            }

            if (step == LeaveStep.StayWithExitEvents)
            {
                CheckExitEvents(loc);
                loc.timeLeft = Singleton<Controller>.Instance.CurrentTimeAndDay;
                Plugin.Log.LogInfo(
                    "[locations] our player left "
                        + $"{Core.getTrueLocationName(loc.name)}; it keeps "
                        + "running for the joiner(s) in it");
            }

            return false;
        }

        private static LeaveFacts LeaveOf(Location loc, bool force)
        {
            var d = Singleton<Dreams>.Instance;
            return new LeaveFacts
            {
                Kept = _kept.Contains(loc),
                Force = force,
                Entered = loc.entered,
                LoadingGame = Core.loadingGame,
                Dreaming = d != null && d.dreaming,
                WantToDream = d != null && d.wantToDream,
            };
        }

        /// <summary>
        /// Host: our player going into a location already entered for a joiner
        /// gets the enter events it would have had.
        /// </summary>
        internal static void BeforeEnter(Location loc, bool force)
        {
            if (HostLocationRules.RunsEnterEvents(EnterOf(loc, force)))
            {
                CheckEnterEvents(loc);
            }
        }

        private static EnterFacts EnterOf(Location loc, bool force)
        {
            return new EnterFacts
            {
                Force = force,
                Entered = loc.entered,
                Kept = _kept.Contains(loc),
                Quiet = _quietEnter.Contains(loc),
            };
        }

        /// <summary>
        /// Host: Location.checkEnterEvents for a location entered for a joiner
        /// is skipped (theirs to run).
        /// </summary>
        internal static bool BeforeEnterEvents(Location loc) =>
            HostLocationRules.RunsGameEnterEvents(_quietEnter.Remove(loc));

        private static readonly MethodInfo ExitEvents =
            AccessTools.Method(typeof(Location), "checkExitEvents");
        private static readonly MethodInfo EnterEvents =
            AccessTools.Method(typeof(Location), "checkEnterEvents");
        private static void CheckExitEvents(Location l)
        {
            try
            {
                MethodInfo exitEvents = ExitEvents;
                if (exitEvents != null)
                {
                    exitEvents.Invoke(l, null);
                }
            }
            catch (System.Exception e)
            {
                System.Exception innerException = e.InnerException;
                string message = null;
                if (innerException != null)
                {
                    message = innerException.Message;
                }

                if (message == null)
                {
                    message = e.Message;
                }

                Plugin.Log.LogWarning(
                    $"[locations] {l.name}'s exit events: {message}");
            }
        }

        private static void CheckEnterEvents(Location l)
        {
            try
            {
                MethodInfo enterEvents = EnterEvents;
                if (enterEvents != null)
                {
                    enterEvents.Invoke(l, null);
                }
            }
            catch (System.Exception e)
            {
                System.Exception innerException = e.InnerException;
                string message = null;
                if (innerException != null)
                {
                    message = innerException.Message;
                }

                if (message == null)
                {
                    message = e.Message;
                }

                Plugin.Log.LogWarning(
                    $"[locations] {l.name}'s enter events: {message}");
            }
        }

        /// <summary>
        /// Client: whether the host runs the area our player is in: the world,
        /// or a separate location the host placed (so it has it at the same
        /// spot; one we loaded after the host didn't answer is ours alone), or
        /// the shared level dream (SharedDream; the host dreams it too). Any
        /// other dream is our own.
        /// </summary>
        public static bool HostRunsHere()
        {
            return PlaceRules.HostRunsHere(HereOf());
        }

        private static HereFacts HereOf()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            bool dreaming = d != null && d.dreaming;
            bool shared = false;
            bool dreamGivenUp = false;
            if (dreaming)
            {
                var loc = SharedDream();
                shared = loc != null;
                if (shared)
                {
                    dreamGivenUp = _spots.GaveUpOn(
                        loc.name, Core.getTrueLocationName(loc.name));
                }
            }

            bool inside = o != null && o.playerInOutsideLocation;
            bool givenUp = false;
            if (!dreaming && inside)
            {
                string name = o.currentLocationName;
                if (name == null)
                {
                    name = "";
                }

                givenUp = _spots.GaveUpOn(
                    name, Core.getTrueLocationName(name));
            }

            return new HereFacts
            {
                Dreaming = dreaming,
                SharedDream = shared,
                DreamGivenUp = dreamGivenUp,
                InLocation = inside,
                LocationGivenUp = givenUp,
            };
        }

        /// <summary>
        /// The dream location our player is in when it is the shared level
        /// dream (World.DreamSync.Shared), else null. The host dreams it first
        /// and every player's copy stands at the host's spot (OnAsk), so, like
        /// a location the host placed, the host runs its characters and events
        /// and the others get the host's.
        /// </summary>
        public static Location SharedDream()
        {
            var d = Singleton<Dreams>.Instance;
            if (d == null)
            {
                return null;
            }

            var dream = new DreamFacts
            {
                Dreaming = d.dreaming,
                HasLocation = d.dreamLocation != null,
                Shared = DreamSync.Shared,
                HasPreset = d.preset != null,
                Preset = PresetName(d),
            };

            if (PlaceRules.IsSharedDream(dream))
            {
                return d.dreamLocation;
            }

            return null;
        }

        private static string PresetName(Dreams d)
        {
            if (d.preset == null)
            {
                return null;
            }

            return d.preset.name;
        }

        // ---- where a player is ----------------------------------------------

        /// <summary>
        /// Where this game's player is: "" in the shared world, the location's
        /// name (its grid's) in a separate location, "dream:" and the dream's
        /// name in a dream.
        /// </summary>
        public static string Current()
        {
            return PlaceRules.Current(WhereOf());
        }

        private static WhereFacts WhereOf()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            WorldGrid instance = Singleton<WorldGrid>.Instance;
            string g = null;
            if (instance != null)
            {
                WorldGrid.Grid currentGrid = instance.currentGrid;
                if (currentGrid != null)
                {
                    g = currentGrid.name;
                }
            }

            return new WhereFacts
            {
                Dreaming = d != null && d.dreaming,
                InLocation = o != null && o.playerInOutsideLocation,
                Grid = g,
            };
        }

        /// <summary>
        /// Whether a player who is away (or not) at this place is in the same
        /// place as our player.
        /// </summary>
        public static bool SamePlace(bool away, string place)
        {
            return PlaceRules.SamePlace(new SameFacts
            {
                Away = away,
                Place = place,
                MeAway = Sync.Players.PlayerCapture.LocalAway(),
                DreamShared = DreamSync.Shared != null,
                Current = Current(),
            });
        }

        // ---- host: answering and loading ------------------------------------

        /// <summary>
        /// Make sure the list of spots has room past the next one (the game has
        /// 21; co-op can use more).
        /// </summary>
        private static void Extend(OutsideLocations o, int need)
        {
            var list = PositionsOf(o);
            if (list == null)
            {
                return;
            }

            while (list.Count < need)
            {
                // More rows of seven, 30000 apart, past the game's own rows (z
                // -80000, -50000, 50000).
                Vec3 spot = LocationSlotRules.ExtraSpot(list.Count);
                list.Add(new Vector3(spot.X, spot.Y, spot.Z));
            }
        }

        /// <summary>
        /// Whether a location of this name exists. The game loads a location as
        /// a scene of that name (LocationMarker.spawnLocation:
        /// SceneManager.LoadScene, then GameObject.Find(name)), and all of
        /// them, dreams too, are the build's scenes under Assets/Locations/.
        /// </summary>
        private static bool IsReal(string name)
        {
            if (_real == null)
            {
                var paths = new List<string>();
                for (int i = 0;
                    i < UnityEngine.SceneManagement.SceneManager
                        .sceneCountInBuildSettings;
                    i++)
                {
                    paths.Add(
                        UnityEngine.SceneManagement.SceneUtility
                            .GetScenePathByBuildIndex(i));
                }

                _real = new LocationNames(paths);
                Plugin.Log.LogInfo(
                    $"[locations] the game has {_real.Count} locations");
            }

            return _real.IsReal(name);
        }

        private static void OnAsk(ulong from, PacketReader r)
        {
            var s = S;
            LocationAsk ask = LocationAskMessage.Read(r);
            string name = ask.Name;
            var o = Singleton<OutsideLocations>.Instance;
            AskAnswer answer = LocationSlotRules.Answer(AskOf(s, o, name));
            if (answer == AskAnswer.Ignore)
            {
                return;
            }

            if (answer == AskAnswer.NoSuchLocation)
            {
                Plugin.Log.LogWarning(
                    $"[locations] refused {s.NameOf(from)}'s ask for "
                        + $"{name}: no such location");
                return;
            }

            if (answer == AskAnswer.Loaded)
            {
                var loc = o.spawnedLocations[name];
                var spot = new Spot
                {
                    Pos = VecOf(loc.transform.position),
                    Turn = LocationSlotRules.TurnOf(
                        loc.transform.eulerAngles.y),
                };

                _spots.Promise(name, spot);
                Plugin.Log.LogInfo(
                    $"[locations] {s.NameOf(from)} goes to {name}: it is "
                        + $"at {spot.Pos.X:0},{spot.Pos.Z:0} here (turned "
                        + $"{spot.Turn})");
            }
            else if (answer == AskAnswer.NewSpot)
            {
                PromiseNew(o, ask, s.NameOf(from));
            }

            _spots.TryGet(name, out var slot);
            var w = s.Begin(MsgType.LocationSlot);
            LocationSlotMessage.Write(
                w, new LocationSlot { Name = name, Spot = slot });
            s.SendTo(from, w, Channel.Control, reliable: true);
        }

        private static AskFacts AskOf(
            Session s, OutsideLocations o, string name)
        {
            bool known = name != null && _spots.TryGet(name, out _);
            bool loaded = false;
            if (o != null && !string.IsNullOrEmpty(name))
            {
                loaded = o.spawnedLocations.TryGetValue(name, out var loc)
                    && loc != null;
            }

            // the scene list is read only when it decides
            bool real = true;
            if (o != null && !string.IsNullOrEmpty(name) && !known && !loaded)
            {
                real = IsReal(name);
            }

            return new AskFacts
            {
                IsHost = s != null && s.IsHost,
                InWorld = WorldShare.InWorld,
                HasLocations = o != null,
                Name = name,
                Known = known,
                Loaded = loaded,
                Real = real,
            };
        }

        /// <summary>
        /// Host: a location we haven't loaded gets our next free spot and a
        /// turn, and (unless it is a dream) we load it too.
        /// </summary>
        private static void PromiseNew(
            OutsideLocations o, LocationAsk ask, string who)
        {
            string name = ask.Name;
            bool dream = ask.Dream;
            Extend(o, LocationSlotRules.Need(o.actualSpawnedLocationsCount));
            Vector3 pos = PositionsOf(o)[o.actualSpawnedLocationsCount];
            var spot = new Spot
            {
                Pos = VecOf(pos),
                Turn = Core.getRandomHalfRotation(),
            };

            o.actualSpawnedLocationsCount++;
            if (LocationSlotRules.LoadsHere(dream))
            {
                _toSpawn.Enqueue(name);
            }

            _spots.Promise(name, spot);
            string line =
                $"[locations] {who} goes to {name}, which "
                    + "we haven't loaded: it gets spot "
                    + $"{spot.Pos.X:0},{spot.Pos.Z:0}, turned "
                    + $"{spot.Turn}";
            string where = "; we load it too";
            if (dream)
            {
                where = " (a dream: theirs alone)";
            }

            Plugin.Log.LogInfo(line + where);
        }

        /// <summary>
        /// Host, every frame: load the locations promised to joiners, one at a
        /// time, when our game isn't busy.
        /// </summary>
        public static void Update()
        {
            var s = S;
            if (s == null || !s.IsHost || !WorldShare.InWorld)
            {
                return;
            }

            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            if (o == null)
            {
                return;
            }

            // The game doesn't refresh its culling while our player is taken to
            // a place: keep the others' areas entered.
            Plugin.Instance.Host.Culling.RefreshWhileLoading();
            if (_spawning != null)
            {
                bool loaded =
                    o.spawnedLocations.TryGetValue(_spawning, out var loc);
                LoadStep step = HostLocationRules.Loading(
                    loaded,
                    new Seconds(Time.realtimeSinceStartup),
                    new Seconds(_spawningSince));
                if (step == LoadStep.Loaded)
                {
                    string at = "(gone)";
                    if (loc != null)
                    {
                        at = "at "
                            + $"{loc.transform.position.x:0},"
                            + $"{loc.transform.position.z:0}, turned "
                            + Mathf.RoundToInt(loc.transform.eulerAngles.y);
                    }

                    string promised = "nothing";
                    if (_spots.TryGet(_spawning, out var slot))
                    {
                        promised =
                            $"{slot.Pos.X:0},{slot.Pos.Z:0}, turned "
                                + $"{slot.Turn}";
                    }

                    // A location that keeps its own turn (dontRotate) or takes
                    // another's (getRotationFromLocation) does so in every game
                    // alike; the promise is kept for the record.
                    Plugin.Log.LogInfo(
                        $"[locations] loaded {_spawning} for the others {at} "
                            + $"(promised {promised})");
                    ScanPathfinding(_spawning);
                    LoadedForOthers++;
                    _spawning = null;
                }
                else if (step == LoadStep.GaveUp)
                {
                    Plugin.Log.LogWarning(
                        $"[locations] {_spawning} didn't load");
                    _spawning = null;
                }

                return;
            }

            KeepRunning(o);
            if (!HostLocationRules.MayStart(StartOf(o, d)))
            {
                return;
            }

            string name = _toSpawn.Dequeue();
            if (!HostLocationRules.MayLoad(
                o.spawnedLocations.ContainsKey(name)))
            {
                return;
            }

            _spawning = name;
            _spawningSince = Time.realtimeSinceStartup;
            WorldGrid instance = Singleton<WorldGrid>.Instance;
            string gridBefore = null;
            if (instance != null)
            {
                WorldGrid.Grid currentGrid = instance.currentGrid;
                if (currentGrid != null)
                {
                    gridBefore = currentGrid.name;
                }
            }

            _gridBefore = gridBefore;
            Plugin.Log.LogInfo(
                $"[locations] loading {name} for the others (we stay where we "
                    + "are)");
            o.StartCoroutine(o.spawnLocation(name, transportAfterSpawn: false));
        }

        private static StartFacts StartOf(OutsideLocations o, Dreams d)
        {
            return new StartFacts
            {
                Queued = _toSpawn.Count,
                Loading = o.loading,
                Dreaming = d != null && d.dreaming,
                DreamPrepared = d != null && d.dreamPrepared,
                SwitchingDream = d != null && d.switchingDream,
            };
        }

        /// <summary>
        /// Host: a location loaded without going there (spawnLocation with
        /// transportAfterSpawn false) has its pathfinding graph made but not
        /// scanned; the game scans it just before it takes its player in. Our
        /// enemies there need it for the joiners, so it is scanned now, as the
        /// game does it.
        /// </summary>
        private static void ScanPathfinding(string name)
        {
            try
            {
                var astar = AstarPath.active;
                Pathfinding.NavGraph graph = null;
                if (astar != null)
                {
                    graph = astar.astarData.GetGraph(name);
                }

                if (graph == null)
                {
                    Plugin.Log.LogInfo(
                        $"[locations] {name} has no pathfinding graph to scan");
                    return;
                }

                int i = System.Array.IndexOf(astar.graphs, graph);
                if (i >= 0)
                {
                    astar.ScanLoop(null, 1 << i);
                }

                Plugin.Log.LogInfo(
                    $"[locations] {name}'s pathfinding graph scanned (graph "
                        + $"{i})");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[locations] couldn't scan {name}'s pathfinding: "
                        + $"{e.Message}");
            }
        }

        /// <summary>
        /// Host: Location.addObjectsToGrid, run as a location loads, makes the
        /// location's grid the current one, as the game expects to go there
        /// next. Loading one for the others, we stay where we were: put it
        /// back.
        /// </summary>
        internal static void AfterAddedToGrid(Location loc)
        {
            if (!HostLocationRules.PutsGridBack(GridOf(loc)))
            {
                return;
            }

            Singleton<WorldGrid>.Instance.setGrid(_gridBefore);
            Plugin.Log.LogInfo(
                $"[locations] {_spawning} added to its grid; the current grid "
                    + $"is {_gridBefore} again");
        }

        private static GridFacts GridOf(Location loc)
        {
            string trueName = null;
            if (loc != null)
            {
                trueName = Core.getTrueLocationName(loc.name);
            }

            var g = Singleton<WorldGrid>.Instance;
            string grid = null;
            bool hasGrid = g != null && g.currentGrid != null;
            if (hasGrid)
            {
                grid = g.currentGrid.name;
            }

            return new GridFacts
            {
                Loading = _spawning,
                HasLocation = loc != null,
                TrueName = trueName,
                Before = _gridBefore,
                HasGrid = hasGrid,
                Grid = grid,
            };
        }

        // ---- client: asking -------------------------------------------------

        /// <summary>
        /// Client: ask the host where a location we haven't loaded stands
        /// (once).
        /// </summary>
        internal static void Ask(string name)
        {
            var s = S;
            if (s == null || !s.IsClient || !_spots.Ask(name))
            {
                return;
            }

            var d = Singleton<Dreams>.Instance;
            var w = s.Begin(MsgType.LocationAsk);
            bool dream = d != null
                && LocationSlotRules.AsksForDream(d.dreamPrepared, d.dreaming);
            LocationAskMessage.Write(
                w, new LocationAsk { Dream = dream, Name = name });
            s.SendToHost(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo(
                $"[locations] asked the host where {name} stands");
        }

        private static void OnSlot(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId)
            {
                return;
            }

            LocationSlot slot = LocationSlotMessage.Read(r);
            string name = slot.Name;
            Vec3 pos = slot.Spot.Pos;
            _spots.Answered(name, slot.Spot);
            Plugin.Log.LogInfo(
                $"[locations] the host's {name} stands at {pos.X:0},{pos.Z:0}, "
                    + $"turned {slot.Spot.Turn}");
        }

        /// <summary>
        /// Whether spawnLocation(name) should wait for the host's answer first.
        /// </summary>
        internal static bool MustWait(string name)
        {
            var s = S;
            var wait = new WaitFacts
            {
                IsClient = s != null && s.IsClient,
                Connected = s != null && s.Connected,
                InWorld = WorldShare.InWorld,
            };

            return _spots.MustWait(wait, name);
        }

        internal static IEnumerator WaitThenSpawn(
            OutsideLocations o, string name, bool transport)
        {
            Ask(name);
            Seconds until = LocationSlotRules.WaitUntil(
                new Seconds(Time.realtimeSinceStartup));
            while (LocationSlotRules.Waits(
                MustWait(name), new Seconds(Time.realtimeSinceStartup), until))
            {
                yield return null;
            }

            if (MustWait(name))
            {
                _spots.GiveUp(name);
                Plugin.Log.LogWarning(
                    $"[locations] the host didn't say where {name} stands: "
                        + "loading it where our game puts it, so it may not "
                        + "match the host's");
            }

            yield return o.StartCoroutine(o.spawnLocation(name, transport));
        }

        // ---- the spot and the turn, as the game loads a location ------------

        internal static void BeforeSpawn(OutsideLocations o)
        {
            if (S != null && S.Active)
            {
                Extend(
                    o, LocationSlotRules.Need(o.actualSpawnedLocationsCount));
            }
        }

        /// <summary>
        /// In spawnLocation, the spot the game picked
        /// (locationPositions[count]): the host's for this location.
        /// </summary>
        public static Vector3 PositionFor(Vector3 picked, string name)
        {
            var s = S;
            bool active = s != null && s.Active;
            if (_spots.TrySpotFor(active, name, out var spot))
            {
                var pos = new Vector3(spot.Pos.X, spot.Pos.Y, spot.Pos.Z);
                if (pos != picked)
                {
                    Plugin.Log.LogInfo(
                        $"[locations] {name} goes to "
                            + $"{pos.x:0},{pos.z:0} (the host's "
                            + $"spot), not {picked.x:0},{picked.z:0}");
                }

                return pos;
            }

            // Host: what our own load uses, turn included, so a joiner who asks
            // meanwhile gets the same.
            bool host = s != null && s.IsHost;
            if (LocationSlotRules.PromisesOwn(active, host, name))
            {
                _spots.Promise(name, new Spot
                {
                    Pos = VecOf(picked),
                    Turn = Core.getRandomHalfRotation(),
                });
            }

            return picked;
        }

        /// <summary>
        /// In LocationMarker.spawnLocation, the location's turn
        /// (Core.getRandomHalfRotation): the host's for this location.
        /// </summary>
        public static int RotationFor(LocationMarker m)
        {
            int rolled = Core.getRandomHalfRotation();
            var s = S;
            string name = null;
            if (m != null)
            {
                name = m.locationName;
            }

            bool active = s != null && s.Active;
            int turn = _spots.TurnFor(active, name, rolled);
            if (!_spots.TrySpotFor(active, name, out _))
            {
                return turn;
            }

            Plugin.Log.LogInfo(
                $"[locations] {name} turned {turn} (the host's), not "
                    + $"{rolled}");
            return turn;
        }
    }

    [HarmonyPatch(typeof(Location), nameof(Location.addObjectsToGrid))]
    static class LocationGridPatch
    {
        static void Postfix(Location __instance) =>
            Locations.AfterAddedToGrid(__instance);
    }

    /// <summary>
    /// Host: a location kept running for a joiner isn't switched off when our
    /// player leaves it.
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.leave))]
    static class LocationKeepPatch
    {
        static bool Prefix(Location __instance, bool force) =>
            Locations.BeforeLeave(__instance, force);
    }

    /// <summary>
    /// Host: our player going into a location already running for a joiner gets
    /// its enter events.
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.enter))]
    static class LocationEnterPatch
    {
        static void Prefix(Location __instance, bool force) =>
            Locations.BeforeEnter(__instance, force);
    }

    /// <summary>
    /// Host: a location entered for a joiner doesn't run its enter events here
    /// (the joiner's game does).
    /// </summary>
    [HarmonyPatch(typeof(Location), "checkEnterEvents")]
    static class LocationQuietEnterPatch
    {
        static bool Prefix(Location __instance) =>
            Locations.BeforeEnterEvents(__instance);
    }

    /// <summary>
    /// Host: the save records a location kept running only for a joiner as our
    /// player left it (entered false), as the game would have it: entered says
    /// our own player went in. Saved as entered, a reload that finds the
    /// location switched off leaves it marked entered, and our player's next
    /// visit (Location.enter, which does nothing for an entered location) would
    /// find it empty.
    /// </summary>
    [HarmonyPatch(
        typeof(Location.SaveState), MethodType.Constructor,
        new[] { typeof(Location) })]
    static class LocationSaveKeptPatch
    {
        static void Postfix(Location.SaveState __instance, Location location)
        {
            if (!HostLocationRules.SavesAsLeft(
                Locations.KeptForOthers(location),
                Locations.OursNow(location)))
            {
                return;
            }

            __instance.entered = false;
        }
    }

    /// <summary>
    /// A client asks the host where a location stands a little early: when the
    /// trip to it starts.
    /// </summary>
    [HarmonyPatch(
        typeof(OutsideLocations), nameof(OutsideLocations.prepareLocation),
        new[] { typeof(string), typeof(Transform) })]
    static class LocationPreparePatch
    {
        static void Prefix(OutsideLocations __instance, string locationName)
        {
            if (Locations.MustWait(locationName)
                && !__instance.spawnedLocations.ContainsKey(locationName))
            {
                Locations.Ask(locationName);
            }
        }
    }

    /// <summary>
    /// A client loads a location only once the host has said where it stands
    /// (or didn't, in time).
    /// </summary>
    [HarmonyPatch(
        typeof(OutsideLocations), nameof(OutsideLocations.spawnLocation),
        new[] { typeof(string), typeof(bool) })]
    static class LocationSpawnPatch
    {
        static bool Prefix(
            OutsideLocations __instance, string locationName,
            bool transportAfterSpawn, ref IEnumerator __result)
        {
            Locations.BeforeSpawn(__instance);
            if (!Locations.MustWait(locationName))
            {
                return true;
            }

            __result = Locations.WaitThenSpawn(
                __instance, locationName, transportAfterSpawn);
            return false;
        }
    }

    /// <summary>
    /// spawnLocation's body: the spot it reads from locationPositions goes
    /// through Locations.PositionFor.
    /// </summary>
    [HarmonyPatch]
    static class LocationSpotPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(
                AccessTools.Method(
                    typeof(OutsideLocations),
                    nameof(OutsideLocations.spawnLocation),
                    new[] { typeof(string), typeof(bool) }));

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var getItem = AccessTools.Method(typeof(List<Vector3>), "get_Item");
            var nameField =
                AccessTools.Field(original.DeclaringType, "locationName");
            if (nameField == null)
            {
                throw new System.Exception(
                    "no locationName in spawnLocation's body");
            }

            var list = code.ToList();
            int i = list.FindIndex(c => c.Calls(getItem));
            if (i < 0)
            {
                throw new System.Exception(
                    "locationPositions[...] not found in spawnLocation's body");
            }

            list.InsertRange(i + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, nameField),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(
                        typeof(Locations), nameof(Locations.PositionFor))),
            });

            return list;
        }
    }

    /// <summary>
    /// LocationMarker.spawnLocation's body: the location's random turn goes
    /// through Locations.RotationFor.
    /// </summary>
    [HarmonyPatch]
    static class LocationTurnPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(
                AccessTools.Method(
                    typeof(LocationMarker),
                    nameof(LocationMarker.spawnLocation),
                    new[] { typeof(GameObject), typeof(bool) }));

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var roll = AccessTools.Method(
                typeof(Core), nameof(Core.getRandomHalfRotation));
            var self = AccessTools.Field(original.DeclaringType, "<>4__this");
            if (self == null)
            {
                throw new System.Exception(
                    "no <>4__this in LocationMarker.spawnLocation's body");
            }

            var list = code.ToList();
            int done = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Calls(roll))
                {
                    continue;
                }

                var labels = list[i].labels;
                list[i] = new CodeInstruction(OpCodes.Ldarg_0)
                {
                    labels = labels,
                    blocks = list[i].blocks
                };

                list.InsertRange(i + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldfld, self),
                    new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(Locations), nameof(Locations.RotationFor))),
                });

                done++;
            }

            if (done == 0)
            {
                throw new System.Exception(
                    "getRandomHalfRotation not found in "
                        + "LocationMarker.spawnLocation's body");
            }

            return list;
        }
    }
}
