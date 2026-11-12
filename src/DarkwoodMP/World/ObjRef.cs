using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Names a world object the same way in every player's game, so a change to it can be sent.
    ///
    ///   Spawned  made while playing together (a dropped item...): a random id its maker gave it, sent with it.
    ///            Also what the world made by itself after a player joined (a location's characters, a delayed
    ///            event's spawn): the host gives it an id and sends it with its name and position (HostIds), and
    ///            each client ties its own copy to that id. A client that hasn't tied one finds it by that name and
    ///            position, as Placed.
    ///   Enemy    one of the host's enemies (its corpse's loot): the host's net id for it
    ///   Saved    anything from the shared save: its save id (SaveableObject.uniqueId), the same for everyone,
    ///            since everyone loaded the host's save. Only ids below SharedIds.SendLimit: a game gives later
    ///            ids out itself, to whatever it makes while it's played, so those differ between games and the
    ///            object is named as Placed instead. A Saved ref above SharedIds.ResolveLimit names nothing.
    ///   Placed   anything else that stands where the level put it: its name and position. The id holds a 24-bit
    ///            hash of the name and the position (x and z, rounded, 20 bits each), and the object is found as
    ///            the one of that name standing nearest to it, within PlacedSlack. So it's still found when it lies
    ///            a little differently in another game (physics settling after loading, a nudge).
    ///
    /// Written as u8 kind, i64 id.
    /// </summary>
    public struct ObjRef
    {
        public enum Kind : byte { None = 0, Saved = 1, Spawned = 2, Enemy = 3, Placed = 4 }

        public Kind K;
        public long Id;

        public bool Valid => K != Kind.None;
        public override string ToString() => $"{K}:{Id}";

        public void Write(PacketWriter w) { w.W.Write((byte)K); w.W.Write(Id); }
        public static ObjRef Read(PacketReader r) => new ObjRef { K = (Kind)r.R.ReadByte(), Id = r.R.ReadInt64() };

        /// <summary>Objects made while playing together, by id, in every game.</summary>
        public static readonly Dictionary<long, GameObject> Spawned = new Dictionary<long, GameObject>();
        private static readonly Dictionary<GameObject, long> _spawnedIds = new Dictionary<GameObject, long>();

        public static void RegisterSpawned(long id, GameObject go)
        {
            Spawned[id] = go;
            _spawnedIds[go] = id;
        }

        public static void ClearSpawned()
        {
            Spawned.Clear(); _spawnedIds.Clear(); _placed.Clear(); _pinned.Clear(); _pinnedIds.Clear();
            _shared.Clear(); _sharedIds.Clear(); _announced.Clear();
        }

        // Ids the host gave to what the world made by itself (HostIds), kept apart from Spawned so code that looks
        // for its own spawns there doesn't see them. On the wire they are Spawned refs.
        private static readonly Dictionary<long, GameObject> _shared = new Dictionary<long, GameObject>();
        private static readonly Dictionary<GameObject, long> _sharedIds = new Dictionary<GameObject, long>();
        // Client: each id the host announced, as the Placed key of the name and position it came with.
        private static readonly Dictionary<long, long> _announced = new Dictionary<long, long>();

        /// <summary>Tie this game's object to an id the host gave out. False if either is tied to something else already.</summary>
        public static bool BindShared(long id, GameObject go)
        {
            if (go == null) return false;
            if (_shared.TryGetValue(id, out var had) && had != null && had != go) return false;
            if (_sharedIds.TryGetValue(go, out long hadId) && hadId != id) return false;
            _shared[id] = go;
            _sharedIds[go] = id;
            return true;
        }

        /// <summary>Client: the host gave out this id for an object of this name at this position.</summary>
        public static void Announced(long id, string name, Vector3 pos) => _announced[id] = PlacedKeyOf(name, pos);

        public static bool IsShared(GameObject go) => go != null && _sharedIds.ContainsKey(go);

        /// <summary>Untie this object from the host's id it was tied to.</summary>
        public static void Unbind(GameObject go)
        {
            if (go == null || !_sharedIds.TryGetValue(go, out long id)) return;
            _sharedIds.Remove(go);
            if (_shared.TryGetValue(id, out var had) && had == go) _shared.Remove(id);
        }

        /// <summary>This id the host gave out is tied to a live object here.</summary>
        public static bool IsBound(long id) => _shared.TryGetValue(id, out var go) && go != null;

        /// <summary>No id names this object in every game: Of would name it by name and position.</summary>
        public static bool Unnamed(GameObject go) => go != null && Of(go).K == Kind.Placed;

        /// <summary>Drop ties to destroyed objects.</summary>
        public static void PruneShared()
        {
            List<long> dead = null;
            foreach (var kv in _shared) if (kv.Value == null) (dead ??= new List<long>()).Add(kv.Key);
            if (dead == null) return;
            foreach (var id in dead) _shared.Remove(id);
            var deadGo = new List<GameObject>();
            foreach (var kv in _sharedIds) if (kv.Key == null) deadGo.Add(kv.Key);
            foreach (var go in deadGo) _sharedIds.Remove(go);
        }

        /// <summary>Host: the live objects it gave ids to, for a joiner who came in after.</summary>
        public static IEnumerable<KeyValuePair<long, GameObject>> SharedObjects
        {
            get { foreach (var kv in _shared) if (kv.Value != null) yield return kv; }
        }

        // A Placed key comes from where the object stands, so one that's moved (dragged) would get a new key in one
        // game but not yet in another. Pinning keeps the key it had when it was first moved, in every game.
        private static readonly Dictionary<long, GameObject> _pinned = new Dictionary<long, GameObject>();
        private static readonly Dictionary<GameObject, long> _pinnedIds = new Dictionary<GameObject, long>();

        /// <summary>Keep this object's current name for it, even after it moves. Call before moving it.</summary>
        public static ObjRef Pin(GameObject go)
        {
            var r = Of(go);
            if (r.K == Kind.Placed && !_pinnedIds.ContainsKey(go)) { _pinned[r.Id] = go; _pinnedIds[go] = r.Id; }
            return r;
        }

        public static ObjRef Of(GameObject go)
        {
            if (go == null) return default;
            if (_spawnedIds.TryGetValue(go, out long sid)) return new ObjRef { K = Kind.Spawned, Id = sid };
            var c = go.GetComponent<Character>();
            if (c != null && NetEnemies.Instance != null && NetEnemies.Instance.NetIdOf(c, out uint eid)) return new ObjRef { K = Kind.Enemy, Id = eid };
            var save = go.GetComponent<SaveableObject>();
            if (save != null && save.uniqueId > 0 && save.uniqueId < SharedIds.SendLimit) return new ObjRef { K = Kind.Saved, Id = save.uniqueId };
            if (_sharedIds.TryGetValue(go, out long hid)) return new ObjRef { K = Kind.Spawned, Id = hid }; // stays as it moves
            if (_pinnedIds.TryGetValue(go, out long pid)) return new ObjRef { K = Kind.Placed, Id = pid };
            return new ObjRef { K = Kind.Placed, Id = PlacedKey(go) };
        }

        /// <summary>How far from its named spot a Placed object is still found.</summary>
        public const float PlacedSlack = 50f;
        private const int Bits = 20, Half = 1 << (Bits - 1), Mask = (1 << Bits) - 1;

        private static int NameHash(string name) => StableHash.Of(name) & 0xFFFFFF;

        private static long PlacedKey(GameObject go) => PlacedKeyOf(go.name, go.transform.position);

        private static long PlacedKeyOf(string name, Vector3 p)
        {
            long x = (Mathf.RoundToInt(p.x) + Half) & Mask, z = (Mathf.RoundToInt(p.z) + Half) & Mask;
            return ((long)NameHash(name) << (2 * Bits)) | (x << Bits) | z;
        }

        private static (int name, float x, float z) Unpack(long id) =>
            ((int)(id >> (2 * Bits)) & 0xFFFFFF, (int)((id >> Bits) & Mask) - Half, (int)(id & Mask) - Half);

        // Placed objects with a component T, by their name's hash, from a scan of the scene for T at most every
        // PlacedRescan seconds. There's one table per component type: when a scan for one type replaced the only
        // table, a trap (Trigger) asked for just after a scripted event (EventTriggers) wasn't found for up to a
        // second, and the host refused a client's trap as gone while its own copy stood there.
        private class PlacedScan { public readonly Dictionary<int, List<GameObject>> ByName = new Dictionary<int, List<GameObject>>(); public float At = -999f; }
        private static readonly Dictionary<System.Type, PlacedScan> _placed = new Dictionary<System.Type, PlacedScan>();
        private const float PlacedRescan = 0.25f;

        // The nearest within PlacedSlack; exact is whether it stands on the named spot (the key rounds to whole units).
        private static GameObject NearestPlaced(PlacedScan scan, long id, out bool exact)
        {
            exact = false;
            var (name, x, z) = Unpack(id);
            if (!scan.ByName.TryGetValue(name, out var list)) return null;
            GameObject best = null; float bestD = PlacedSlack * PlacedSlack;
            foreach (var go in list)
            {
                if (go == null) continue;
                var p = go.transform.position;
                float d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                if (d <= bestD) { bestD = d; best = go; }
            }
            exact = best != null && bestD <= 1f;
            return best;
        }

        private static int _unsharedLogged;

        /// <summary>Find the object in this game; T is the component it's expected to have.</summary>
        public static T Resolve<T>(ObjRef r) where T : Component
        {
            GameObject go = null;
            switch (r.K)
            {
                case Kind.Spawned:
                    if (Spawned.TryGetValue(r.Id, out go) && go != null) break;
                    if (_shared.TryGetValue(r.Id, out go) && go != null) break;
                    // An id the host gave out that this game hasn't tied to its copy yet: find it as Placed, and tie it.
                    if (_announced.TryGetValue(r.Id, out long key))
                    {
                        var found = Resolve<T>(new ObjRef { K = Kind.Placed, Id = key });
                        go = found != null ? found.gameObject : null;
                        if (go != null && !_sharedIds.ContainsKey(go)) BindShared(r.Id, go);
                    }
                    break;
                case Kind.Enemy:
                    var c = NetEnemies.Instance?.CharacterOf((uint)r.Id);
                    go = c != null ? c.gameObject : null;
                    break;
                case Kind.Saved:
                    if (r.Id >= SharedIds.ResolveLimit)
                    {
                        if (_unsharedLogged++ < 20) Plugin.Log.LogWarning($"A ref to save id {r.Id} came in, but only ids below {SharedIds.ResolveLimit} are the same in every game: ignored");
                        break;
                    }
                    var sm = Singleton<SaveManager>.Instance;
                    if (sm != null && sm.uniqueIdDict.TryGetValue((int)r.Id, out var t) && t != null) go = t.gameObject;
                    break;
                case Kind.Placed:
                    if (_pinned.TryGetValue(r.Id, out go) && go != null) break;
                    if (!_placed.TryGetValue(typeof(T), out var scan)) _placed[typeof(T)] = scan = new PlacedScan();
                    go = NearestPlaced(scan, r.Id, out bool exact);
                    // Scan again when it isn't on its spot too: one made since the last scan (a swamper's splats
                    // and their traps spread over a few seconds) was otherwise taken for its neighbour within
                    // PlacedSlack, and the host set off the neighbour's trap event instead of the one stepped on.
                    if ((go == null || !exact || go.GetComponent<T>() == null) && Time.unscaledTime - scan.At > PlacedRescan)
                    {
                        scan.At = Time.unscaledTime;
                        scan.ByName.Clear();
                        foreach (var comp in Object.FindObjectsOfType<T>(true)) // inactive too: a location the host loaded for a joiner but isn't in
                        {
                            int h = NameHash(comp.gameObject.name);
                            if (!scan.ByName.TryGetValue(h, out var list)) scan.ByName[h] = list = new List<GameObject>();
                            list.Add(comp.gameObject);
                        }
                        go = NearestPlaced(scan, r.Id, out exact);
                    }
                    if (go != null && !exact && WorldActions.Debug)
                        Plugin.Log.LogInfo($"[world] Placed:{r.Id} ({typeof(T).Name}) taken as {go.name} at {go.transform.position}, off its spot");
                    break;
            }
            return go != null ? go.GetComponent<T>() : null;
        }
    }
}
