using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Separate locations (caves, interiors) and dreams stand in the same place in every game.
    ///
    /// The game loads a location the first time a player goes there (OutsideLocations.spawnLocation): its marker goes
    /// to the next free one of a list of far-off spots (locationPositions[actualSpawnedLocationsCount]) and the
    /// location is turned by Core.getRandomHalfRotation (LocationMarker.spawnLocation). So each game would put a
    /// location first visited after joining wherever its own visiting order and dice put it. Here the host decides:
    /// a joiner about to load a location it hasn't got asks the host (LocationAsk) and loads it where the host says
    /// (LocationSlot: the spot and the turn). A location the host has loaded is where it is in the host's game. One
    /// the host hasn't got is given the host's next free spot and a turn, and (unless it is a dream) the host loads it
    /// too, without going there, so the host can judge the joiner's looting and doors there like the world's, and the
    /// location ends up in the host's save. The host's own later visit loads it where it promised.
    ///
    /// Players are in the same place when both are in the shared world, or both in the same location (by name). Dreams
    /// are each player's own, except the shared level dream (World.DreamSync): players in it see each other there.
    /// </summary>
    public static class Locations
    {
        private static Session S => Session.Instance;

        private struct Slot { public Vector3 Pos; public int Rot; public bool HasRot; }

        /// <summary>Where each location stands. Host: what it promised or used; client: what the host said.</summary>
        private static readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();
        /// <summary>Client: locations the host didn't answer for in time (loaded where our own game puts them).</summary>
        private static readonly HashSet<string> _gaveUp = new HashSet<string>();
        /// <summary>Client: asked, no answer yet.</summary>
        private static readonly HashSet<string> _asked = new HashSet<string>();
        /// <summary>Host: locations promised to a joiner, to load in our game when it isn't busy.</summary>
        private static readonly Queue<string> _toSpawn = new Queue<string>();
        private static string _spawning;
        private static string _gridBefore;
        private static float _spawningSince;

        public const float AskTimeout = 10f;

        /// <summary>Host: how many locations we loaded for joiners (for the tests).</summary>
        public static int LoadedForOthers;
        /// <summary>Host: a location promised to a joiner is still to load.</summary>
        public static bool Busy => _spawning != null || _toSpawn.Count > 0;

        private static readonly AccessTools.FieldRef<OutsideLocations, List<Vector3>> PositionsOf =
            AccessTools.FieldRefAccess<OutsideLocations, List<Vector3>>("locationPositions");

        public static void Register(Session s)
        {
            s.Register(MsgType.LocationAsk, OnAsk);
            s.Register(MsgType.LocationSlot, OnSlot);
            s.OnSessionEnded += () => { _slots.Clear(); _gaveUp.Clear(); _asked.Clear(); _toSpawn.Clear(); _spawning = null; _kept.Clear(); _quietEnter.Clear(); };
        }

        // ---- host: running a location for the joiners in it ----------------------------------------

        /// <summary>Host: separate locations kept entered (running) because a joiner is in them.</summary>
        private static readonly HashSet<Location> _kept = new HashSet<Location>();
        /// <summary>Host: locations we entered for a joiner: their enter events are the joiner's game's to run, not ours.</summary>
        private static readonly HashSet<Location> _quietEnter = new HashSet<Location>();
        private static float _nextKeep;
        /// <summary>Half the size of a location's grid (OutsideLocations.createGrid: six chunks a side), and less than
        /// half the distance between spots, so a player is in the location whose marker is this near.</summary>
        private const float LocationReach = 15000f;

        /// <summary>Host: whether we keep this location running for a joiner in it (our player may be elsewhere).</summary>
        public static bool KeptForOthers(Location l) => l != null && _kept.Contains(l);

        /// <summary>Host: whether our own player is in this separate location now.</summary>
        public static bool OursNow(Location l)
        {
            var o = Singleton<OutsideLocations>.Instance;
            return l != null && o != null && o.playerInOutsideLocation
                   && Core.getTrueLocationName(o.currentLocationName ?? "") == Core.getTrueLocationName(l.name);
        }

        /// <summary>Host: how many locations are kept running for joiners (for the tests).</summary>
        public static int KeptCount => _kept.Count(l => l != null);

        /// <summary>
        /// Host: the separate location a joiner who says they're at `place` (their PlayerState) and stands at pos is in,
        /// when we have it at that spot; null for the world, a dream (each player's own), or a location we haven't got
        /// (they loaded it before the host answered: it stands somewhere else here).
        /// </summary>
        public static Location HostRunsAt(string place, Vector3 pos)
        {
            if (string.IsNullOrEmpty(place) || place == "?" || place.StartsWith("dream:")) return null;
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null) return null;
            Location loc = null;
            foreach (var kv in o.spawnedLocations)
                if (kv.Value != null && (kv.Key == place || Core.getTrueLocationName(kv.Key) == place)) { loc = kv.Value; break; }
            if (loc == null) return null;
            var d = pos - loc.transform.position;
            return d.x * d.x + d.z * d.z < LocationReach * LocationReach ? loc : null;
        }

        /// <summary>
        /// Host, twice a second: the locations joiners are in are entered (their objects and characters switched on, as
        /// the game does when its player goes there) even when our player isn't there, so our game runs their enemies
        /// and NetEnemies sends them to those joiners. One nobody is in any more is left again, unless our player is in it.
        /// </summary>
        /// <summary>Kept locations whose loose objects DragSync has noted (once each time it starts being kept).</summary>
        private static readonly HashSet<Location> _noted = new HashSet<Location>();

        private static void KeepRunning(OutsideLocations o)
        {
            if (Time.unscaledTime < _nextKeep) return;
            _nextKeep = Time.unscaledTime + 0.5f;
            var want = new HashSet<Location>();
            foreach (var p in Combat.NetPlayerProxy.All.Values)
                if (p != null && p.InLocation != null) want.Add(p.InLocation);
            foreach (var loc in want)
            {
                if (_kept.Add(loc)) Plugin.Log.LogInfo($"[locations] keeping {Core.getTrueLocationName(loc.name)} running for the joiner(s) in it{(loc.entered ? " (already entered)" : "")}");
                if (!loc.entered || !loc.gameObject.activeInHierarchy)
                {
                    _quietEnter.Add(loc);
                    loc.enter(force: !loc.gameObject.activeInHierarchy);
                }
                if (_noted.Add(loc)) DragSync.Instance?.NoteAll(loc.gameObject);
            }
            _kept.RemoveWhere(l => l == null);
            _noted.RemoveWhere(l => l == null || !_kept.Contains(l));
            foreach (var loc in _kept.Where(l => !want.Contains(l)).ToList())
            {
                _kept.Remove(loc);
                _quietEnter.Remove(loc);
                string name = Core.getTrueLocationName(loc.name);
                bool ours = OursNow(loc);
                Plugin.Log.LogInfo($"[locations] nobody else is in {name} any more{(ours ? "; our player is, so it stays" : ": left")}");
                if (ours) continue;
                loc.leave(force: true);
                Singleton<WorldGrid>.Instance?.getGrid(name)?.leave();
            }
        }

        /// <summary>
        /// Host: Location.leave on a location kept for a joiner (our player going back to the world, or on to another
        /// location: OutsideLocations.leaveAllLocations) doesn't switch it off. Our player's exit events still run.
        /// </summary>
        internal static bool BeforeLeave(Location loc, bool force)
        {
            if (!_kept.Contains(loc))
            {
                _quietEnter.Remove(loc);
                return true;
            }
            var d = Singleton<Dreams>.Instance;
            if (!force && loc.entered && !Core.loadingGame && !(d != null && (d.dreaming || d.wantToDream)))
            {
                CheckExitEvents(loc);
                loc.timeLeft = Singleton<Controller>.Instance.CurrentTimeAndDay;
                Plugin.Log.LogInfo($"[locations] our player left {Core.getTrueLocationName(loc.name)}; it keeps running for the joiner(s) in it");
            }
            return false;
        }

        /// <summary>Host: our player going into a location already entered for a joiner gets the enter events it would have had.</summary>
        internal static void BeforeEnter(Location loc, bool force)
        {
            if (!force && loc.entered && _kept.Contains(loc) && !_quietEnter.Contains(loc)) CheckEnterEvents(loc);
        }

        /// <summary>Host: Location.checkEnterEvents for a location entered for a joiner is skipped (theirs to run).</summary>
        internal static bool BeforeEnterEvents(Location loc) => !_quietEnter.Remove(loc);

        private static readonly MethodInfo ExitEvents = AccessTools.Method(typeof(Location), "checkExitEvents");
        private static readonly MethodInfo EnterEvents = AccessTools.Method(typeof(Location), "checkEnterEvents");
        private static void CheckExitEvents(Location l)
        {
            try { ExitEvents?.Invoke(l, null); } catch (System.Exception e) { Plugin.Log.LogWarning($"[locations] {l.name}'s exit events: {e.InnerException?.Message ?? e.Message}"); }
        }
        private static void CheckEnterEvents(Location l)
        {
            try { EnterEvents?.Invoke(l, null); } catch (System.Exception e) { Plugin.Log.LogWarning($"[locations] {l.name}'s enter events: {e.InnerException?.Message ?? e.Message}"); }
        }

        /// <summary>
        /// Client: whether the host runs the area our player is in: the world, or a separate location the host placed
        /// (so it has it at the same spot; one we loaded after the host didn't answer is ours alone). A dream never is.
        /// </summary>
        public static bool HostRunsHere()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            if (d != null && d.dreaming) return false;
            if (o == null || !o.playerInOutsideLocation) return true;
            string name = o.currentLocationName ?? "";
            return !_gaveUp.Contains(name) && !_gaveUp.Contains(Core.getTrueLocationName(name));
        }

        // ---- where a player is ------------------------------------------------------------------

        /// <summary>
        /// Where this game's player is: "" in the shared world, the location's name (its grid's) in a separate
        /// location, "dream:" and the dream's name in a dream.
        /// </summary>
        public static string Current()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            bool dream = d != null && d.dreaming;
            if (!dream && !(o != null && o.playerInOutsideLocation)) return "";
            string g = Singleton<WorldGrid>.Instance?.currentGrid?.name ?? "?";
            return dream ? "dream:" + g : g;
        }

        /// <summary>Whether a player who is away (or not) at this place is in the same place as our player.</summary>
        public static bool SamePlace(bool away, string place)
        {
            bool meAway = Sync.PlayerCapture.LocalAway();
            if (!away || !meAway) return away == meAway;
            if (string.IsNullOrEmpty(place) || place == "?") return false;
            if (place.StartsWith("dream:") && DreamSync.Shared == null) return false; // someone else's own dream
            return place == Current();
        }

        // ---- host: answering and loading ---------------------------------------------------------

        /// <summary>Make sure the list of spots has room past the next one (the game has 21; co-op can use more).</summary>
        private static void Extend(OutsideLocations o, int need)
        {
            var list = PositionsOf(o);
            if (list == null) return;
            while (list.Count < need)
            {
                // More rows of seven, 30000 apart, past the game's own rows (z -80000, -50000, 50000).
                int extra = list.Count - 21; if (extra < 0) extra = 0;
                int row = extra / 7, col = extra % 7;
                float z = row % 2 == 0 ? 80000f + 30000f * (row / 2) : -110000f - 30000f * (row / 2);
                list.Add(new Vector3(-75000f + 25000f * col, 0f, z));
            }
        }

        private static void OnAsk(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsHost || !WorldShare.InWorld) return;
            bool dream = (r.R.ReadByte() & 1) != 0;
            string name = r.ReadStr();
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null || string.IsNullOrEmpty(name)) return;
            if (!_slots.TryGetValue(name, out var slot) || !slot.HasRot)
            {
                if (o.spawnedLocations.TryGetValue(name, out var loc) && loc != null)
                {
                    slot = new Slot { Pos = loc.transform.position, Rot = Mathf.RoundToInt(loc.transform.eulerAngles.y), HasRot = true };
                    Plugin.Log.LogInfo($"[locations] {s.NameOf(from)} goes to {name}: it is at {slot.Pos.x:0},{slot.Pos.z:0} here (turned {slot.Rot})");
                }
                else
                {
                    Extend(o, o.actualSpawnedLocationsCount + 8);
                    slot = new Slot { Pos = PositionsOf(o)[o.actualSpawnedLocationsCount], Rot = Core.getRandomHalfRotation(), HasRot = true };
                    o.actualSpawnedLocationsCount++;
                    if (!dream) _toSpawn.Enqueue(name);
                    Plugin.Log.LogInfo($"[locations] {s.NameOf(from)} goes to {name}, which we haven't loaded: it gets spot {slot.Pos.x:0},{slot.Pos.z:0}, turned {slot.Rot}" +
                                       (dream ? " (a dream: theirs alone)" : "; we load it too"));
                }
                _slots[name] = slot;
            }
            var w = s.Begin(MsgType.LocationSlot);
            w.WriteStr(name);
            w.Write(slot.Pos);
            w.W.Write((short)slot.Rot);
            s.SendTo(from, w, Channel.Control, reliable: true);
        }

        /// <summary>Host, every frame: load the locations promised to joiners, one at a time, when our game isn't busy.</summary>
        public static void Update()
        {
            var s = S;
            if (s == null || !s.IsHost || !WorldShare.InWorld) return;
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            if (o == null) return;
            // The game doesn't refresh its culling while our player is taken to a place: keep the others' areas entered.
            Combat.SharedCullingPatch.RefreshWhileLoading();
            if (_spawning != null)
            {
                if (o.spawnedLocations.TryGetValue(_spawning, out var loc))
                {
                    string at = loc != null ? $"at {loc.transform.position.x:0},{loc.transform.position.z:0}, turned {Mathf.RoundToInt(loc.transform.eulerAngles.y)}" : "(gone)";
                    string promised = _slots.TryGetValue(_spawning, out var slot) ? $"{slot.Pos.x:0},{slot.Pos.z:0}, turned {slot.Rot}" : "nothing";
                    // A location that keeps its own turn (dontRotate) or takes another's (getRotationFromLocation)
                    // does so in every game alike; the promise is kept for the record.
                    Plugin.Log.LogInfo($"[locations] loaded {_spawning} for the others {at} (promised {promised})");
                    ScanPathfinding(_spawning);
                    LoadedForOthers++;
                    _spawning = null;
                }
                else if (Time.realtimeSinceStartup - _spawningSince > 60f) { Plugin.Log.LogWarning($"[locations] {_spawning} didn't load"); _spawning = null; }
                return;
            }
            KeepRunning(o);
            if (_toSpawn.Count == 0 || o.loading || (d != null && (d.dreaming || d.dreamPrepared || d.switchingDream))) return;
            string name = _toSpawn.Dequeue();
            if (o.spawnedLocations.ContainsKey(name)) return;
            _spawning = name; _spawningSince = Time.realtimeSinceStartup;
            _gridBefore = Singleton<WorldGrid>.Instance?.currentGrid?.name;
            Plugin.Log.LogInfo($"[locations] loading {name} for the others (we stay where we are)");
            o.StartCoroutine(o.spawnLocation(name, transportAfterSpawn: false));
        }

        /// <summary>
        /// Host: a location loaded without going there (spawnLocation with transportAfterSpawn false) has its
        /// pathfinding graph made but not scanned; the game scans it just before it takes its player in. Our enemies
        /// there need it for the joiners, so it is scanned now, as the game does it.
        /// </summary>
        private static void ScanPathfinding(string name)
        {
            try
            {
                var astar = AstarPath.active;
                var graph = astar != null ? astar.astarData.GetGraph(name) : null;
                if (graph == null) { Plugin.Log.LogInfo($"[locations] {name} has no pathfinding graph to scan"); return; }
                int i = System.Array.IndexOf(astar.graphs, graph);
                if (i >= 0) astar.ScanLoop(null, 1 << i);
                Plugin.Log.LogInfo($"[locations] {name}'s pathfinding graph scanned (graph {i})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[locations] couldn't scan {name}'s pathfinding: {e.Message}"); }
        }

        /// <summary>
        /// Host: Location.addObjectsToGrid, run as a location loads, makes the location's grid the current one, as
        /// the game expects to go there next. Loading one for the others, we stay where we were: put it back.
        /// </summary>
        internal static void AfterAddedToGrid(Location loc)
        {
            if (_spawning == null || loc == null || _gridBefore == null || Core.getTrueLocationName(loc.name) != _spawning) return;
            var g = Singleton<WorldGrid>.Instance;
            if (g == null || g.currentGrid == null || g.currentGrid.name == _gridBefore) return;
            g.setGrid(_gridBefore);
            Plugin.Log.LogInfo($"[locations] {_spawning} added to its grid; the current grid is {_gridBefore} again");
        }

        // ---- client: asking --------------------------------------------------------------------

        /// <summary>Client: ask the host where a location we haven't loaded stands (once).</summary>
        internal static void Ask(string name)
        {
            var s = S;
            if (s == null || !s.IsClient || _slots.ContainsKey(name) || !_asked.Add(name)) return;
            var d = Singleton<Dreams>.Instance;
            var w = s.Begin(MsgType.LocationAsk);
            w.W.Write((byte)(d != null && (d.dreamPrepared || d.dreaming) ? 1 : 0));
            w.WriteStr(name);
            s.SendToHost(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo($"[locations] asked the host where {name} stands");
        }

        private static void OnSlot(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId) return;
            string name = r.ReadStr();
            var pos = r.ReadVector3();
            int rot = r.R.ReadInt16();
            _slots[name] = new Slot { Pos = pos, Rot = rot, HasRot = true };
            _asked.Remove(name);
            Plugin.Log.LogInfo($"[locations] the host's {name} stands at {pos.x:0},{pos.z:0}, turned {rot}");
        }

        /// <summary>Whether spawnLocation(name) should wait for the host's answer first.</summary>
        internal static bool MustWait(string name)
        {
            var s = S;
            return s != null && s.IsClient && s.Connected && WorldShare.InWorld && !_slots.ContainsKey(name) && !_gaveUp.Contains(name);
        }

        internal static IEnumerator WaitThenSpawn(OutsideLocations o, string name, bool transport)
        {
            Ask(name);
            float until = Time.realtimeSinceStartup + AskTimeout;
            while (MustWait(name) && Time.realtimeSinceStartup < until) yield return null;
            if (MustWait(name))
            {
                _gaveUp.Add(name);
                Plugin.Log.LogWarning($"[locations] the host didn't say where {name} stands: loading it where our game puts it, so it may not match the host's");
            }
            yield return o.StartCoroutine(o.spawnLocation(name, transport));
        }

        // ---- the spot and the turn, as the game loads a location ----------------------------------

        internal static void BeforeSpawn(OutsideLocations o)
        {
            if (S != null && S.Active) Extend(o, o.actualSpawnedLocationsCount + 8);
        }

        /// <summary>In spawnLocation, the spot the game picked (locationPositions[count]): the host's for this location.</summary>
        public static Vector3 PositionFor(Vector3 picked, string name)
        {
            var s = S;
            if (s == null || !s.Active || name == null) return picked;
            if (_slots.TryGetValue(name, out var slot))
            {
                if (slot.Pos != picked) Plugin.Log.LogInfo($"[locations] {name} goes to {slot.Pos.x:0},{slot.Pos.z:0} (the host's spot), not {picked.x:0},{picked.z:0}");
                return slot.Pos;
            }
            // Host: what our own load uses, turn included, so a joiner who asks meanwhile gets the same.
            if (s.IsHost) _slots[name] = new Slot { Pos = picked, Rot = Core.getRandomHalfRotation(), HasRot = true };
            return picked;
        }

        /// <summary>In LocationMarker.spawnLocation, the location's turn (Core.getRandomHalfRotation): the host's for this location.</summary>
        public static int RotationFor(LocationMarker m)
        {
            int rolled = Core.getRandomHalfRotation();
            var s = S;
            string name = m != null ? m.locationName : null;
            if (s == null || !s.Active || name == null) return rolled;
            if (!_slots.TryGetValue(name, out var slot) || !slot.HasRot) return rolled;
            Plugin.Log.LogInfo($"[locations] {name} turned {slot.Rot} (the host's), not {rolled}");
            return slot.Rot;
        }
    }

    [HarmonyPatch(typeof(Location), nameof(Location.addObjectsToGrid))]
    static class LocationGridPatch
    {
        static void Postfix(Location __instance) => Locations.AfterAddedToGrid(__instance);
    }

    /// <summary>Host: a location kept running for a joiner isn't switched off when our player leaves it.</summary>
    [HarmonyPatch(typeof(Location), nameof(Location.leave))]
    static class LocationKeepPatch
    {
        static bool Prefix(Location __instance, bool force) => Locations.BeforeLeave(__instance, force);
    }

    /// <summary>Host: our player going into a location already running for a joiner gets its enter events.</summary>
    [HarmonyPatch(typeof(Location), nameof(Location.enter))]
    static class LocationEnterPatch
    {
        static void Prefix(Location __instance, bool force) => Locations.BeforeEnter(__instance, force);
    }

    /// <summary>Host: a location entered for a joiner doesn't run its enter events here (the joiner's game does).</summary>
    [HarmonyPatch(typeof(Location), "checkEnterEvents")]
    static class LocationQuietEnterPatch
    {
        static bool Prefix(Location __instance) => Locations.BeforeEnterEvents(__instance);
    }

    /// <summary>
    /// Host: the save records a location kept running only for a joiner as our player left it (entered false), as
    /// the game would have it: entered says our own player went in. Saved as entered, a reload that finds the
    /// location switched off leaves it marked entered, and our player's next visit (Location.enter, which does
    /// nothing for an entered location) would find it empty.
    /// </summary>
    [HarmonyPatch(typeof(Location.SaveState), MethodType.Constructor, new[] { typeof(Location) })]
    static class LocationSaveKeptPatch
    {
        static void Postfix(Location.SaveState __instance, Location location)
        {
            if (!Locations.KeptForOthers(location) || Locations.OursNow(location)) return;
            __instance.entered = false;
        }
    }

    /// <summary>A client asks the host where a location stands a little early: when the trip to it starts.</summary>
    [HarmonyPatch(typeof(OutsideLocations), nameof(OutsideLocations.prepareLocation), new[] { typeof(string), typeof(Transform) })]
    static class LocationPreparePatch
    {
        static void Prefix(OutsideLocations __instance, string locationName)
        {
            if (Locations.MustWait(locationName) && !__instance.spawnedLocations.ContainsKey(locationName)) Locations.Ask(locationName);
        }
    }

    /// <summary>A client loads a location only once the host has said where it stands (or didn't, in time).</summary>
    [HarmonyPatch(typeof(OutsideLocations), nameof(OutsideLocations.spawnLocation), new[] { typeof(string), typeof(bool) })]
    static class LocationSpawnPatch
    {
        static bool Prefix(OutsideLocations __instance, string locationName, bool transportAfterSpawn, ref IEnumerator __result)
        {
            Locations.BeforeSpawn(__instance);
            if (!Locations.MustWait(locationName)) return true;
            __result = Locations.WaitThenSpawn(__instance, locationName, transportAfterSpawn);
            return false;
        }
    }

    /// <summary>spawnLocation's body: the spot it reads from locationPositions goes through Locations.PositionFor.</summary>
    [HarmonyPatch]
    static class LocationSpotPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(OutsideLocations), nameof(OutsideLocations.spawnLocation), new[] { typeof(string), typeof(bool) }));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var getItem = AccessTools.Method(typeof(List<Vector3>), "get_Item");
            var nameField = AccessTools.Field(original.DeclaringType, "locationName");
            if (nameField == null) throw new System.Exception("no locationName in spawnLocation's body");
            var list = code.ToList();
            int done = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (done == 0 && list[i].Calls(getItem))
                {
                    list.InsertRange(i + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, nameField),
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Locations), nameof(Locations.PositionFor))),
                    });
                    done++;
                }
            }
            if (done != 1) throw new System.Exception("locationPositions[...] not found in spawnLocation's body");
            return list;
        }
    }

    /// <summary>LocationMarker.spawnLocation's body: the location's random turn goes through Locations.RotationFor.</summary>
    [HarmonyPatch]
    static class LocationTurnPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(LocationMarker), nameof(LocationMarker.spawnLocation), new[] { typeof(GameObject), typeof(bool) }));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var roll = AccessTools.Method(typeof(Core), nameof(Core.getRandomHalfRotation));
            var self = AccessTools.Field(original.DeclaringType, "<>4__this");
            if (self == null) throw new System.Exception("no <>4__this in LocationMarker.spawnLocation's body");
            var list = code.ToList();
            int done = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Calls(roll)) continue;
                var labels = list[i].labels;
                list[i] = new CodeInstruction(OpCodes.Ldarg_0) { labels = labels, blocks = list[i].blocks };
                list.InsertRange(i + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldfld, self),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Locations), nameof(Locations.RotationFor))),
                });
                done++;
            }
            if (done == 0) throw new System.Exception("getRandomHalfRotation not found in LocationMarker.spawnLocation's body");
            return list;
        }
    }
}
