using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Places
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
    /// Where a player is, and whether players are in the same place, is
    /// Whereabouts. What happens is the core's LocationSlotRules and
    /// HostLocationRules; LocationPatches hand the game's calls here.
    /// </summary>
    internal sealed partial class Locations
    {
        private readonly CoopSession _coop;

        /// <summary>
        /// Host: the locations kept running for the joiners in them.
        /// </summary>
        public readonly KeptLocations Kept;

        /// <summary>
        /// Where each location stands. Host: what it promised or used; client:
        /// what the host said, the names asked for, and the names given up on
        /// (loaded where our own game puts them).
        /// </summary>
        private readonly LocationSpots _spots = new LocationSpots();
        /// <summary>
        /// Host: locations promised to a joiner, to load in our game when it
        /// isn't busy.
        /// </summary>
        private readonly Queue<string> _toSpawn = new Queue<string>();
        private string _spawning;
        private string _gridBefore;
        private float _spawningSince;
        private LocationNames _real;

        /// <summary>
        /// Host: how many locations we loaded for joiners (for the tests).
        /// </summary>
        public int LoadedForOthers { get; private set; }
        /// <summary>
        /// Host: a location promised to a joiner is still to load.
        /// </summary>
        public bool Busy => _spawning != null || _toSpawn.Count > 0;

        private static readonly
            AccessTools.FieldRef<OutsideLocations, List<Vector3>> PositionsOf =
                AccessTools.FieldRefAccess<OutsideLocations, List<Vector3>>(
                    "locationPositions");

        public Locations(CoopSession coop)
        {
            _coop = coop;
            Kept = new KeptLocations(coop);
            coop.Handle(MsgType.LocationAsk, OnAsk);
            coop.Handle(MsgType.LocationSlot, OnSlot);
        }

        private Session S => _coop.Net;

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// Client: whether we gave up on this location (by its name or its true
        /// name) and loaded it where our own game puts it.
        /// </summary>
        public bool GaveUpOn(string name, string trueName) =>
            _spots.GaveUpOn(name, trueName);

        // ---- host: answering and loading ------------------------------------

        /// <summary>
        /// Make sure the list of spots has room past the next one (the game has
        /// 21; co-op can use more).
        /// </summary>
        private void Extend(OutsideLocations o, int need)
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
        private bool IsReal(string name)
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

        private void OnAsk(ulong from, PacketReader r)
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

        private AskFacts AskOf(
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
        private void PromiseNew(
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
        public void Update()
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

            Kept.KeepRunning(o);
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

        private StartFacts StartOf(OutsideLocations o, Dreams d)
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
        private void ScanPathfinding(string name)
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
        internal void AfterAddedToGrid(Location loc)
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

        private GridFacts GridOf(Location loc)
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

        // ---- the spot and the turn, as the game loads a location ------------

        /// <summary>
        /// OutsideLocations.spawnLocation: the list of spots gets room past
        /// the next one, and a client loads a location only once the host has
        /// said where it stands (or didn't, in time). Returns what the game
        /// runs instead, or null for the game's own load.
        /// </summary>
        internal IEnumerator SpawnInstead(
            OutsideLocations o, string name, bool transport)
        {
            if (S != null && S.Active)
            {
                Extend(
                    o, LocationSlotRules.Need(o.actualSpawnedLocationsCount));
            }

            if (!MustWait(name))
            {
                return null;
            }

            return WaitThenSpawn(o, name, transport);
        }

        /// <summary>
        /// OutsideLocations.prepareLocation: a client asks the host where a
        /// location stands a little early, when the trip to it starts.
        /// </summary>
        internal void BeforePrepare(OutsideLocations o, string name)
        {
            bool loaded = name != null
                && o.spawnedLocations.ContainsKey(name);
            if (LocationSlotRules.AsksEarly(MustWait(name), loaded))
            {
                Ask(name);
            }
        }

        /// <summary>
        /// In spawnLocation, the spot the game picked
        /// (locationPositions[count]): the host's for this location.
        /// </summary>
        public Vector3 PositionFor(Vector3 picked, string name)
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
        /// In LocationMarker.spawnLocation, the location's turn: the host's for
        /// this location, else what Core.getRandomHalfRotation rolled.
        /// </summary>
        public int RotationFor(LocationMarker m, int rolled)
        {
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
}
