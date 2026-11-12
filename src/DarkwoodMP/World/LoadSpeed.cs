using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Makes loading a saved world faster without changing what it loads. Measured with --dwmp-profile-load, most
    /// of the time went to three things the game does slowly, not to work it has to do:
    ///
    /// 1. After making the saved objects, SaveManager.Load walks its 21,000-entry loadedObjects dictionary with
    ///    Values.ElementAt(i) and Keys.ElementAt(i). Those can't index a dictionary, so each call counts from the
    ///    start: about 450 million steps. Here they read from an array copy of each collection instead (made
    ///    again whenever its size changes, and dropped when the load ends).
    /// 2. WorldGrid.registerToNode, called for each of about 8,000 objects as a grid registers them on load, looks
    ///    through all of the grid's objects to see if it's already listed, and through all of its nodes to find the
    ///    one it's in. While a grid registers its objects, both are looked up in an index instead; the node found
    ///    is the same one (the first in the grid's list that the object is in).
    ///
    /// 3. Load reads the save's two JSON files (about 11 MB) and parses them on the main thread, about 1.3 s,
    ///    right after the game's fixed 1.6 s wait (below). They're parsed on a background thread during that wait
    ///    instead, and Load takes the result if the text it read is the same; otherwise, or if the background
    ///    parse failed, it parses as before.
    /// 4. Core.AddPrefab(path) calls Resources.Load for each of about 19,000 saved objects, though there are only
    ///    a few hundred kinds. While a world loads, each path's prefab is looked up once.
    ///
    /// (The game's fixed 1.6 s wait before loading is left alone: it lets the black screen finish fading out
    /// before Load stops every running tween, DOTween.KillAll. Cut short, the fade stops half-way and the whole
    /// game stays dim.)
    /// </summary>
    public static class LoadSpeed
    {
        // ---- 1. ElementAt on the dictionary's keys and values ---------------------------------------------------

        private static readonly Dictionary<object, (int count, Array copy)> _copies = new Dictionary<object, (int, Array)>();

        public static T ElementAt<T>(IEnumerable<T> source, int index)
        {
            if (!(source is ICollection<T> c)) return Enumerable.ElementAt(source, index);
            if (!_copies.TryGetValue(source, out var e) || e.count != c.Count || !(e.copy is T[]))
                _copies[source] = e = (c.Count, source.ToArray());
            return ((T[])e.copy)[index];
        }

        internal static void Forget() => _copies.Clear();

        // ---- 3. the save's JSON, parsed while the screen fades -----------------------------------------------

        private sealed class Ahead<T> where T : class
        {
            public string Path, Text; public T Result; public System.Threading.Thread Thread;
        }
        private static Ahead<SaveManager.DynamicSave> _dynamic;
        private static Ahead<SaveManager.StaticSave> _static;
        public static int AheadUsed, AheadMissed;

        /// <summary>The game is about to load the current profile's world: start parsing its files.</summary>
        internal static void ParseAhead()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null) return;
            _dynamic = Start<SaveManager.DynamicSave>(sm.dynamicFile);
            _static = Start<SaveManager.StaticSave>(sm.staticFile);
        }

        private static Ahead<T> Start<T>(string path) where T : class
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            var a = new Ahead<T> { Path = path };
            a.Thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // Shared for writing and deleting: on Windows a plain read would make the game's own write of
                    // this file fail while the read lasts. If the game changes it meanwhile, Take sees other text.
                    using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                               System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                    using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                        a.Text = sr.ReadToEnd();
                    a.Result = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(a.Text);
                }
                catch { a.Result = null; } // encrypted, or something that needs the main thread: Load parses it
            }) { IsBackground = true, Name = "DarkwoodMP save parse" };
            a.Thread.Start();
            return a;
        }

        public static SaveManager.DynamicSave Dynamic(string text) => Take(ref _dynamic, text);
        public static SaveManager.StaticSave Static(string text) => Take(ref _static, text);

        private static T Take<T>(ref Ahead<T> slot, string text) where T : class
        {
            var a = slot; slot = null; // each parse is used once: a later Load (the backup file) parses itself
            if (a != null)
            {
                a.Thread.Join();
                if (a.Result != null && string.Equals(a.Text, text, StringComparison.Ordinal)) { AheadUsed++; return a.Result; }
                AheadMissed++;
            }
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(text);
        }

        // ---- 4. Resources.Load once per prefab path while loading -------------------------------------------

        internal static bool Loading;
        private static readonly Dictionary<string, UnityEngine.Object> _prefabs = new Dictionary<string, UnityEngine.Object>();
        public static int PrefabLookups, PrefabLoads;

        internal static UnityEngine.Object Prefab(string path)
        {
            PrefabLookups++;
            if (!_prefabs.TryGetValue(path, out var o) || o == null) { _prefabs[path] = o = Resources.Load("Prefabs/" + path); PrefabLoads++; }
            return o;
        }

        internal static void LoadStarted() { Loading = true; PrefabLookups = PrefabLoads = 0; }

        internal static void LoadEnded()
        {
            Loading = false;
            _prefabs.Clear(); // not held after loading, so the game can unload what it no longer uses
            Plugin.Log.LogInfo($"LoadSpeed: {PrefabLookups} prefabs made from {PrefabLoads} loads; save parsed ahead: {AheadUsed} used, {AheadMissed} not");
            AheadUsed = AheadMissed = 0;
        }

        // ---- 2. WorldGrid.registerToNode during a grid's registerCullablesOnLoad ---------------------------------

        internal static bool Batch;
        /// <summary>"--dwmp-verify-load": also run the game's own search for each object and count differences.</summary>
        public static bool Verify;
        public static int Checked, Mismatches;
        private static WorldGrid.Grid _grid;
        private static HashSet<CullableObject> _listed;
        private static Dictionary<long, List<int>> _cells;
        private static float _cell;

        internal static void BeginBatch() { Batch = true; _grid = null; }
        internal static void EndBatch() { Batch = false; _grid = null; _listed = null; _cells = null; }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private static void Index(WorldGrid w)
        {
            _grid = w.currentGrid;
            _listed = new HashSet<CullableObject>();
            foreach (var co in _grid.cullables) if (co != null) _listed.Add(co);
            _cell = Mathf.Max(1, w.distance);
            _cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < _grid.nodes.Count; i++)
            {
                var p = _grid.nodes[i].position;
                long k = Key(Mathf.FloorToInt(p.x / _cell), Mathf.FloorToInt(p.y / _cell));
                if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<int>();
                list.Add(i);
            }
        }

        /// <summary>
        /// The same as WorldGrid.registerToNodes (an object over several nodes: one point per node spacing across
        /// its size), with the nodes near each point looked up instead of searched. The cullable goes to the same
        /// nodes, and they're listed on the object in the grid's order, as the game's loop over all nodes does.
        /// </summary>
        internal static bool RegisterToNodes(WorldGrid w, GameObject GO, Vector2 size, WorldGrid.Cullable.Type type)
        {
            if (w.currentGrid == null) return true;
            if (!ReferenceEquals(w.currentGrid, _grid)) Index(w);

            var co = GO.GetComponent<CullableObject>();
            if (co == null) co = GO.AddComponent<CullableObject>();
            co.registered = true;
            var cullable = new WorldGrid.Cullable(GO, type);
            if (GO.GetComponent<Location>() != null) cullable.isLocation = true;
            if (GO.GetComponent<MapElement>() != null) cullable.isMapElement = true;
            if (_listed.Add(co) && !_grid.cullables.Contains(co)) _grid.cullables.Add(co);

            int d = w.distance;
            int across = ((int)size.x + d) / d, down = ((int)size.y + d) / d;
            Vector3 corner = new Vector3(GO.transform.position.x - size.x / 2f, 0f, GO.transform.position.z - size.y / 2f);
            if (GO.GetComponent<Collider>() != null)
            {
                var box = GO.GetComponent<BoxCollider>();
                corner = new Vector3(box.bounds.center.x - box.bounds.size.x / 2f, 0f, box.bounds.center.z - box.bounds.size.z / 2f);
            }
            float half = d / 2;
            var hit = new SortedSet<int>();
            for (int i = 0; i < across * down; i++)
            {
                float px = corner.x + d * (i % across), pz = corner.z + d * (i / across);
                int x0 = Mathf.FloorToInt((px - half) / _cell), x1 = Mathf.FloorToInt((px + half) / _cell);
                int z0 = Mathf.FloorToInt((pz - half) / _cell), z1 = Mathf.FloorToInt((pz + half) / _cell);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!_cells.TryGetValue(Key(x, z), out var list)) continue;
                        foreach (int n in list)
                        {
                            var np = _grid.nodes[n].position;
                            if (Mathf.Abs(px - np.x) <= half && Mathf.Abs(pz - np.y) <= half) hit.Add(n);
                        }
                    }
            }
            if (Verify)
            {
                var expected = new List<int>();
                for (int n = 0; n < _grid.nodes.Count; n++)
                {
                    var np = _grid.nodes[n].position;
                    for (int i = 0; i < across * down; i++)
                    {
                        float px = corner.x + d * (i % across), pz = corner.z + d * (i / across);
                        if (Mathf.Abs(px - np.x) <= half && Mathf.Abs(pz - np.y) <= half) { expected.Add(n); break; }
                    }
                }
                Checked++;
                if (!expected.SequenceEqual(hit)) { Mismatches++; if (Mismatches <= 5) Plugin.Log.LogWarning($"LoadSpeed: {GO.name}: nodes {string.Join(",", hit)}, the game's search {string.Join(",", expected)}"); }
            }
            foreach (int n in hit)
            {
                var node = _grid.nodes[n];
                node.cullables.Add(cullable); // a new Cullable is never already listed
                if (!co.nodes.Contains(node)) co.nodes.Add(node);
            }
            return false;
        }

        /// <summary>
        /// The same as WorldGrid.registerToClosestNode: the first node at the smallest distance (under 10,000),
        /// with the distance worked out in place instead of through Core.trueDistance's conversions.
        /// </summary>
        internal static bool RegisterToClosestNode(WorldGrid w, GameObject GO)
        {
            var nodes = w.currentGrid?.nodes;
            if (nodes == null || nodes.Count == 0) return true;
            Vector3 pos = GO.transform.position;
            var at = new Vector2(pos.x, pos.z);
            int index = 0; float best = 10000f;
            for (int i = 0; i < nodes.Count; i++)
            {
                float dist = Vector2.Distance(at, nodes[i].position);
                if (dist < best) { best = dist; index = i; }
            }
            nodes[index].cullables.Add(new WorldGrid.Cullable(GO));
            return false;
        }

        /// <summary>The same as WorldGrid.registerToNode, with its two searches looked up instead.</summary>
        internal static bool RegisterToNode(WorldGrid w, GameObject GO, WorldGrid.Cullable.Type type)
        {
            if (w.currentGrid == null) return true;
            if (!ReferenceEquals(w.currentGrid, _grid)) Index(w);

            var co = GO.GetComponent<CullableObject>();
            if (co == null) co = GO.AddComponent<CullableObject>();
            if (co.registered && co.node != null) co.node.removeCullable(GO);
            co.registered = true;
            if (GO.GetComponent<Location>() != null) return false;
            var cullable = new WorldGrid.Cullable(GO, type);
            if (GO.GetComponent<MapElement>() != null) cullable.isMapElement = true;
            if (_listed.Add(co) && !_grid.cullables.Contains(co)) _grid.cullables.Add(co);

            // The first node (in the grid's order) whose square, distance/2 each way, holds the object.
            float half = w.distance / 2;
            Vector3 pos = GO.transform.position;
            int best = int.MaxValue;
            int x0 = Mathf.FloorToInt((pos.x - half) / _cell), x1 = Mathf.FloorToInt((pos.x + half) / _cell);
            int z0 = Mathf.FloorToInt((pos.z - half) / _cell), z1 = Mathf.FloorToInt((pos.z + half) / _cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (int i in list)
                    {
                        if (i >= best) continue;
                        var np = _grid.nodes[i].position;
                        if (Mathf.Abs(pos.x - np.x) <= half && Mathf.Abs(pos.z - np.y) <= half) best = i;
                    }
                }
            if (Verify)
            {
                int expected = int.MaxValue;
                for (int i = 0; i < _grid.nodes.Count; i++)
                {
                    var np = _grid.nodes[i].position;
                    if (Mathf.Abs(pos.x - np.x) <= half && Mathf.Abs(pos.z - np.y) <= half) { expected = i; break; }
                }
                Checked++;
                if (expected != best) { Mismatches++; if (Mismatches <= 5) Plugin.Log.LogWarning($"LoadSpeed: {GO.name} at {pos}: node {best}, the game's search {expected}"); }
            }
            if (best != int.MaxValue)
            {
                var node = _grid.nodes[best];
                node.cullables.Add(cullable); // a new Cullable is never already listed (it has no Equals of its own)
                co.node = node;
                return false;
            }
            w.registerToClosestNode(GO);
            return false;
        }
    }

    /// <summary>SaveManager.Load's loop over loadedObjects: ElementAt from an array copy (see LoadSpeed).</summary>
    [HarmonyPatch]
    static class LoadElementAtPatch
    {
        static MethodBase TargetMethod()
        {
            var t = typeof(SaveManager).GetNestedTypes(BindingFlags.NonPublic).FirstOrDefault(n => n.Name.StartsWith("<Load>d__"));
            return t == null ? null : AccessTools.Method(t, "MoveNext");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var fast = AccessTools.Method(typeof(LoadSpeed), nameof(LoadSpeed.ElementAt));
            var dyn = AccessTools.Method(typeof(LoadSpeed), nameof(LoadSpeed.Dynamic));
            var stat = AccessTools.Method(typeof(LoadSpeed), nameof(LoadSpeed.Static));
            int n = 0, parses = 0;
            foreach (var ins in code)
            {
                if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodInfo m)
                {
                    if (m.Name == "ElementAt" && m.DeclaringType == typeof(Enumerable) && m.IsGenericMethod)
                    {
                        ins.opcode = OpCodes.Call;
                        ins.operand = fast.MakeGenericMethod(m.GetGenericArguments());
                        n++;
                    }
                    // JsonConvert.DeserializeObject<DynamicSave or StaticSave>(string): the parse made ahead.
                    else if (m.Name == "DeserializeObject" && m.DeclaringType == typeof(Newtonsoft.Json.JsonConvert) && m.IsGenericMethod
                             && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string))
                    {
                        var t = m.GetGenericArguments()[0];
                        if (t == typeof(SaveManager.DynamicSave)) { ins.operand = dyn; parses++; }
                        else if (t == typeof(SaveManager.StaticSave)) { ins.operand = stat; parses++; }
                    }
                }
                yield return ins;
            }
            if (n == 0) Plugin.Log.LogWarning("LoadSpeed: SaveManager.Load has no ElementAt calls to speed up (game changed?)");
            if (parses == 0) Plugin.Log.LogWarning("LoadSpeed: SaveManager.Load parses no save files (game changed?)");
        }

        static void Prefix() { if (!LoadSpeed.Loading) LoadSpeed.LoadStarted(); }

        static void Postfix(bool __result)
        {
            if (__result) return;
            LoadSpeed.Forget();
            LoadSpeed.LoadEnded();
        }
    }

    /// <summary>The game is about to load a saved world (after its 1.6 s fade): parse the save meanwhile.</summary>
    [HarmonyPatch(typeof(WorldGenerator), "Start")]
    static class ParseAheadPatch
    {
        static void Postfix() { if (Core.loadingGame) LoadSpeed.ParseAhead(); }
    }

    /// <summary>Core.AddPrefab(path, ...) while loading: each path's prefab is looked up once (see LoadSpeed).</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class AddPrefabCachePatch
    {
        static bool Prefix(string prefab, Vector3 position, Quaternion quaternion, GameObject parentGO, bool worldSpace, ref GameObject __result)
        {
            if (!LoadSpeed.Loading) return true;
            __result = Core.AddPrefab(LoadSpeed.Prefab(prefab), position, quaternion, parentGO, worldSpace);
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGrid.Grid), nameof(WorldGrid.Grid.registerCullablesOnLoad))]
    static class GridRegisterBatchPatch
    {
        static void Prefix() => LoadSpeed.BeginBatch();
        static Exception Finalizer(Exception __exception) { LoadSpeed.EndBatch(); return __exception; }
    }

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.registerToNodes), new[] { typeof(GameObject), typeof(Vector2), typeof(WorldGrid.Cullable.Type) })]
    static class RegisterToNodesPatch
    {
        static bool Prefix(WorldGrid __instance, GameObject GO, Vector2 size, WorldGrid.Cullable.Type type)
        {
            if (!LoadSpeed.Batch) return true;
            try { return LoadSpeed.RegisterToNodes(__instance, GO, size, type); }
            catch (Exception e) { Plugin.Log.LogWarning($"LoadSpeed: {e.Message}; the game's own registerToNodes instead"); LoadSpeed.EndBatch(); return true; }
        }
    }

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.registerToClosestNode))]
    static class RegisterToClosestNodePatch
    {
        static bool Prefix(WorldGrid __instance, GameObject GO) => !LoadSpeed.Batch || LoadSpeed.RegisterToClosestNode(__instance, GO);
    }

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.registerToNode), new[] { typeof(GameObject), typeof(WorldGrid.Cullable.Type) })]
    static class RegisterToNodePatch
    {
        static bool Prefix(WorldGrid __instance, GameObject GO, WorldGrid.Cullable.Type type)
        {
            if (!LoadSpeed.Batch) return true;
            try { return LoadSpeed.RegisterToNode(__instance, GO, type); }
            catch (Exception e) { Plugin.Log.LogWarning($"LoadSpeed: {e.Message}; the game's own registerToNode instead"); LoadSpeed.EndBatch(); return true; }
        }
    }
}
