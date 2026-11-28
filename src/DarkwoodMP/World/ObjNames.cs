using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The tables ObjRef names objects by, for one session (CoopSession.Names):
    /// what was made while playing together, the ids the host gave out
    /// (HostIds) and the ones it announced, pinned keys, and the scans the
    /// Placed lookup searches. ObjRefRules decides; this reads the game and
    /// keeps the tables. With no session, ObjRef uses one with empty tables
    /// (ModHost.AloneNames), so a lookup still finds saved and placed objects.
    /// </summary>
    internal sealed class ObjNames
    {
        // null for the tables used with no session
        private readonly CoopSession _coop;

        public ObjNames(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>
        /// Objects made while playing together, by id, in every game.
        /// </summary>
        public readonly Dictionary<long, GameObject> Spawned =
            new Dictionary<long, GameObject>();
        private readonly Dictionary<GameObject, long> _spawnedIds =
            new Dictionary<GameObject, long>();

        public void RegisterSpawned(long id, GameObject go)
        {
            Spawned[id] = go;
            _spawnedIds[go] = id;
        }

        // Ids the host gave to what the world made by itself (HostIds), kept
        // apart from Spawned so code that looks for its own spawns there
        // doesn't see them. On the wire they are Spawned refs.
        private readonly Dictionary<long, GameObject> _shared =
            new Dictionary<long, GameObject>();
        private readonly Dictionary<GameObject, long> _sharedIds =
            new Dictionary<GameObject, long>();
        // Client: each id the host announced, as the Placed key of the name and
        // position it came with.
        private readonly Dictionary<long, long> _announced =
            new Dictionary<long, long>();

        /// <summary>
        /// Tie this game's object to an id the host gave out. False if either
        /// is tied to something else already.
        /// </summary>
        public bool BindShared(long id, GameObject go)
        {
            if (!ObjRefRules.MayTie(TieOf(id, go)))
            {
                return false;
            }

            _shared[id] = go;
            _sharedIds[go] = id;
            return true;
        }

        /// What tying go to the host's id looks at.
        private TieFacts TieOf(long id, GameObject go)
        {
            if (go == null)
            {
                return new TieFacts { Id = id };
            }

            bool idTied = _shared.TryGetValue(id, out var had) && had != null;
            bool objectTied = _sharedIds.TryGetValue(go, out long hadId);
            return new TieFacts
            {
                Id = id,
                Exists = true,
                IdTied = idTied,
                IdTiedHere = idTied && had == go,
                ObjectTied = objectTied,
                ObjectId = hadId,
            };
        }

        /// <summary>
        /// Client: the host gave out this id for an object of this name at this
        /// position.
        /// </summary>
        public void Announced(long id, string name, Vector3 pos) =>
            _announced[id] = PlacedKeyOf(name, pos);

        public bool IsShared(GameObject go) =>
            go != null && _sharedIds.ContainsKey(go);

        /// <summary>
        /// Untie this object from the host's id it was tied to.
        /// </summary>
        public void Unbind(GameObject go)
        {
            if (go == null || !_sharedIds.TryGetValue(go, out long id))
            {
                return;
            }

            _sharedIds.Remove(go);
            if (_shared.TryGetValue(id, out var had) && had == go)
            {
                _shared.Remove(id);
            }
        }

        /// <summary>
        /// This id the host gave out is tied to a live object here.
        /// </summary>
        public bool IsBound(long id) =>
            _shared.TryGetValue(id, out var go) && go != null;

        /// <summary>
        /// No id names this object in every game: Of would name it by name and
        /// position.
        /// </summary>
        public bool Unnamed(GameObject go) =>
            go != null && Of(go).K == ObjRef.Kind.Placed;

        /// <summary>Drop ties to destroyed objects.</summary>
        public void PruneShared()
        {
            List<long> dead = null;
            foreach (var kv in _shared)
            {
                if (kv.Value == null)
                {
                    if (dead == null)
                    {
                        dead = new List<long>();
                    }

                    dead.Add(kv.Key);
                }
            }

            if (dead == null)
            {
                return;
            }

            foreach (var id in dead)
            {
                _shared.Remove(id);
            }

            var deadGo = new List<GameObject>();
            foreach (var kv in _sharedIds)
            {
                if (kv.Key == null)
                {
                    deadGo.Add(kv.Key);
                }
            }

            foreach (var go in deadGo)
            {
                _sharedIds.Remove(go);
            }
        }

        /// <summary>
        /// Host: the live objects it gave ids to, for a joiner who came in
        /// after.
        /// </summary>
        public IEnumerable<KeyValuePair<long, GameObject>> SharedObjects
        {
            get
            {
                foreach (var kv in _shared)
                {
                    if (kv.Value != null)
                    {
                        yield return kv;
                    }
                }
            }
        }

        // A Placed key comes from where the object stands, so one that's moved
        // (dragged) would get a new key in one game but not yet in another.
        // Pinning keeps the key it had when it was first moved, in every game.
        private readonly Dictionary<long, GameObject> _pinned =
            new Dictionary<long, GameObject>();
        private readonly Dictionary<GameObject, long> _pinnedIds =
            new Dictionary<GameObject, long>();

        /// <summary>
        /// Keep this object's current name for it, even after it moves. Call
        /// before moving it.
        /// </summary>
        public ObjRef Pin(GameObject go)
        {
            var r = Of(go);
            if (r.K == ObjRef.Kind.Placed && !_pinnedIds.ContainsKey(go))
            {
                _pinned[r.Id] = go;
                _pinnedIds[go] = r.Id;
            }

            return r;
        }

        public ObjRef Of(GameObject go)
        {
            NamingFacts facts = FactsOf(go);
            if (ObjRefRules.NeedsPlace(facts))
            {
                facts = facts with
                {
                    NameHash = StableHash.Of(go.name),
                    Pos = GroundOf(go.transform.position),
                };
            }

            return ObjRef.RefOf(ObjRefRules.Of(facts));
        }

        /// <summary>
        /// What naming go looks at, apart from its name and position
        /// (ObjRefRules.NeedsPlace says when those are read).
        /// </summary>
        private NamingFacts FactsOf(GameObject go)
        {
            if (go == null)
            {
                return default;
            }

            bool spawned = _spawnedIds.TryGetValue(go, out long sid);
            uint eid = 0;
            bool enemy = false;
            var c = go.GetComponent<Character>();
            // no session's enemies when playing alone (Host.AloneNames)
            if (c != null && _coop != null && _coop.Enemies != null)
            {
                enemy = _coop.Enemies.NetIdOf(c, out eid);
            }

            int saveId = 0;
            var save = go.GetComponent<SaveableObject>();
            if (save != null)
            {
                saveId = save.uniqueId;
            }

            bool shared = _sharedIds.TryGetValue(go, out long hid);
            bool pinned = _pinnedIds.TryGetValue(go, out long pid);
            return new NamingFacts
            {
                Exists = true,
                Spawned = spawned,
                SpawnedId = sid,
                Enemy = enemy,
                EnemyId = eid,
                SaveId = saveId,
                SendLimit = SharedIds.SendLimit,
                Shared = shared,
                SharedId = hid,
                Pinned = pinned,
                PinnedKey = pid,
            };
        }

        private static Vec2 GroundOf(Vector3 p)
        {
            return new Vec2(p.x, p.z);
        }

        private static long PlacedKeyOf(string name, Vector3 p)
        {
            return ObjRefRules.PlacedKey(StableHash.Of(name), GroundOf(p));
        }

        // Placed objects with a component T, by their name's hash, from a scan
        // of the scene for T at most every PlacedRescan seconds. There's one
        // table per component type: when a scan for one type replaced the only
        // table, a trap (Trigger) asked for just after a scripted event
        // (EventTriggers) wasn't found for up to a second, and the host refused
        // a client's trap as gone while its own copy stood there.
        private class PlacedScan
        {
            public readonly Dictionary<int, List<GameObject>> ByName =
                new Dictionary<int, List<GameObject>>();
            public float At = ObjRefRules.NeverScanned;
        }

        private readonly Dictionary<System.Type, PlacedScan> _placed =
            new Dictionary<System.Type, PlacedScan>();

        // The live objects of one name in a scan, and where each stands, for
        // ObjRefRules.Nearest.
        private readonly List<GameObject> _near =
            new List<GameObject>();
        private readonly List<Vec2> _nearAt = new List<Vec2>();

        // The nearest within PlacedSlack, and whether it stands on the named
        // spot (the key rounds to whole units).
        private GameObject NearestPlaced(
            PlacedScan scan, long id, out bool exact)
        {
            exact = false;
            PlacedSpot spot = ObjRefRules.Unpack(id);
            if (!scan.ByName.TryGetValue(spot.Name, out var list))
            {
                return null;
            }

            _near.Clear();
            _nearAt.Clear();
            foreach (var go in list)
            {
                if (go != null)
                {
                    _near.Add(go);
                    _nearAt.Add(GroundOf(go.transform.position));
                }
            }

            PlacedMatch match = ObjRefRules.Nearest(spot.Spot, _nearAt);
            if (!match.Found)
            {
                return null;
            }

            exact = match.OnSpot;
            return _near[match.Index];
        }

        private int _unsharedLogged;

        /// <summary>
        /// Find the object in this game; T is the component it's expected to
        /// have.
        /// </summary>
        public T Resolve<T>(ObjRef r) where T : Component
        {
            GameObject go = null;
            switch (ObjRefRules.SourceOf(LookupOf(r)))
            {
                case ObjSource.Spawned:
                    go = Spawned[r.Id];
                    break;
                case ObjSource.Shared:
                    go = _shared[r.Id];
                    break;
                case ObjSource.Announced:
                    go = FromAnnounced<T>(r.Id);
                    break;
                case ObjSource.Enemy:
                    go = EnemyOf(r.Id);
                    break;
                case ObjSource.Unshared:
                    WarnUnshared(r.Id);
                    break;
                case ObjSource.SaveId:
                    go = SavedOf(r.Id);
                    break;
                case ObjSource.Pinned:
                    go = _pinned[r.Id];
                    break;
                case ObjSource.Nearest:
                    go = NearestOf<T>(r.Id);
                    break;
            }

            if (go != null)
            {
                return go.GetComponent<T>();
            }

            return null;
        }

        /// What Resolve looks at to choose where to find r.
        private LookupFacts LookupOf(ObjRef r)
        {
            bool spawned = Spawned.TryGetValue(r.Id, out var made)
                && made != null;
            bool shared = _shared.TryGetValue(r.Id, out var tied)
                && tied != null;
            bool pinned = _pinned.TryGetValue(r.Id, out var kept)
                && kept != null;
            return new LookupFacts
            {
                Kind = (ObjKind)r.K,
                Id = r.Id,
                SpawnedAlive = spawned,
                SharedAlive = shared,
                Announced = _announced.ContainsKey(r.Id),
                PinnedAlive = pinned,
                ResolveLimit = SharedIds.ResolveLimit,
            };
        }

        // An id the host gave out that this game hasn't tied to its copy yet:
        // find it as Placed, and tie it.
        private GameObject FromAnnounced<T>(long id) where T : Component
        {
            var found = Resolve<T>(new ObjRef
            {
                K = ObjRef.Kind.Placed,
                Id = _announced[id]
            });

            GameObject go = null;
            if (found != null)
            {
                go = found.gameObject;
            }

            if (ObjRefRules.TiesAnnounced(TieOf(id, go)))
            {
                _shared[id] = go;
                _sharedIds[go] = id;
                if (_coop != null)
                {
                    _coop.HostIds.Place(id, go);
                }
            }

            return go;
        }

        private static GameObject EnemyOf(long id)
        {
            NetEnemies instance = null;
            if (Coop.Current != null)
            {
                instance = Coop.Current.Enemies;
            }

            Character c = null;
            if (instance != null)
            {
                c = instance.CharacterOf(ObjRefRules.EnemyIdOf(id));
            }

            if (c != null)
            {
                return c.gameObject;
            }

            return null;
        }

        private void WarnUnshared(long id)
        {
            if (_unsharedLogged++ < 20)
            {
                Plugin.Log.LogWarning(
                    $"A ref to save id {id} came in, but only "
                        + $"ids below {SharedIds.ResolveLimit} are "
                        + "the same in every game: ignored");
            }
        }

        private static GameObject SavedOf(long id)
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm != null
                && sm.uniqueIdDict.TryGetValue(
                    ObjRefRules.SaveIdOf(id),
                    out var t)
                && t != null)
            {
                return t.gameObject;
            }

            return null;
        }

        private GameObject NearestOf<T>(long id) where T : Component
        {
            if (!_placed.TryGetValue(typeof(T), out var scan))
            {
                _placed[typeof(T)] = scan = new PlacedScan();
            }

            var go = NearestPlaced(scan, id, out bool exact);
            // Scan again when it isn't on its spot too: one made since the
            // last scan (a swamper's splats and their traps spread over a few
            // seconds) was otherwise taken for its neighbour within
            // PlacedSlack, and the host set off the neighbour's trap event
            // instead of the one stepped on.
            float now = Time.unscaledTime;
            var facts = new ScanFacts
            {
                Found = go != null,
                OnSpot = exact,
                HasComponent = go != null && exact
                    && go.GetComponent<T>() != null,
                Now = new Seconds(now),
                ScannedAt = new Seconds(scan.At),
            };

            if (ObjRefRules.ScanAgain(facts))
            {
                scan.At = now;
                scan.ByName.Clear();
                // inactive too: a location the host loaded for a joiner but
                // isn't in
                foreach (var comp in Object.FindObjectsOfType<T>(true))
                {
                    int h = ObjRefRules.NameKey(
                        StableHash.Of(comp.gameObject.name));
                    if (!scan.ByName.TryGetValue(h, out var list))
                    {
                        scan.ByName[h] = list = new List<GameObject>();
                    }

                    list.Add(comp.gameObject);
                }

                go = NearestPlaced(scan, id, out exact);
            }

            if (go != null && !exact && WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] Placed:{id} ({typeof(T).Name}) taken "
                        + $"as {go.name} at {go.transform.position}, "
                        + "off its spot");
            }

            return go;
        }
    }
}
