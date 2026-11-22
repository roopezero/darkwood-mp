using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Loading
{
    /// LoadSpeed: the culling grid's registration, batched while loading.
    public sealed partial class LoadSpeed
    {
        // ---- 2. WorldGrid.registerToNode in registerCullablesOnLoad ---------
        // During a grid's registerCullablesOnLoad.

        internal bool Batch;
        /// <summary>
        /// "--dwmp-verify-load": also run the game's own search for each object
        /// and count differences.
        /// </summary>
        public bool Verify;
        public int Checked, Mismatches;
        private WorldGrid.Grid _grid;
        private HashSet<CullableObject> _listed;
        private Dictionary<long, List<int>> _cells;
        private float _cell;

        internal void BeginBatch()
        {
            Batch = true;
            _grid = null;
        }

        internal void EndBatch()
        {
            Batch = false;
            _grid = null;
            _listed = null;
            _cells = null;
        }

        private CullableObject CullableOf(GameObject go)
        {
            var co = go.GetComponent<CullableObject>();
            if (co != null)
            {
                return co;
            }

            return go.AddComponent<CullableObject>();
        }

        private long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private void Index(WorldGrid w)
        {
            _grid = w.currentGrid;
            _listed = new HashSet<CullableObject>();
            foreach (var co in _grid.cullables)
            {
                if (co != null)
                {
                    _listed.Add(co);
                }
            }

            _cell = Mathf.Max(1, w.distance);
            _cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < _grid.nodes.Count; i++)
            {
                var p = _grid.nodes[i].position;
                long k = Key(
                    Mathf.FloorToInt(p.x / _cell),
                    Mathf.FloorToInt(p.y / _cell));
                if (!_cells.TryGetValue(k, out var list))
                {
                    _cells[k] = list = new List<int>();
                }

                list.Add(i);
            }
        }

        /// <summary>
        /// The same as WorldGrid.registerToNodes (an object over several nodes:
        /// one point per node spacing across its size), with the nodes near
        /// each point looked up instead of searched. The cullable goes to the
        /// same nodes, and they're listed on the object in the grid's order, as
        /// the game's loop over all nodes does.
        /// </summary>
        internal bool RegisterToNodes(
            WorldGrid w, GameObject GO, Vector2 size,
            WorldGrid.Cullable.Type type)
        {
            if (w.currentGrid == null)
            {
                return true;
            }

            if (!ReferenceEquals(w.currentGrid, _grid))
            {
                Index(w);
            }

            var co = CullableOf(GO);
            co.registered = true;
            var cullable = new WorldGrid.Cullable(GO, type);
            if (GO.GetComponent<Location>() != null)
            {
                cullable.isLocation = true;
            }

            if (GO.GetComponent<MapElement>() != null)
            {
                cullable.isMapElement = true;
            }

            if (_listed.Add(co) && !_grid.cullables.Contains(co))
            {
                _grid.cullables.Add(co);
            }

            int d = w.distance;
            int across = ((int)size.x + d) / d, down = ((int)size.y + d) / d;
            Vector3 corner = new Vector3(
                GO.transform.position.x - size.x / 2f, 0f,
                GO.transform.position.z - size.y / 2f);
            if (GO.GetComponent<Collider>() != null)
            {
                var box = GO.GetComponent<BoxCollider>();
                corner = new Vector3(
                    box.bounds.center.x - box.bounds.size.x / 2f, 0f,
                    box.bounds.center.z - box.bounds.size.z / 2f);
            }

            float half = d / 2;
            var hit = new SortedSet<int>();
            for (int i = 0; i < across * down; i++)
            {
                float px = corner.x + d * (i % across),
                    pz = corner.z + d * (i / across);
                int x0 = Mathf.FloorToInt((px - half) / _cell),
                    x1 = Mathf.FloorToInt((px + half) / _cell);
                int z0 = Mathf.FloorToInt((pz - half) / _cell),
                    z1 = Mathf.FloorToInt((pz + half) / _cell);
                for (int x = x0; x <= x1; x++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!_cells.TryGetValue(Key(x, z), out var list))
                        {
                            continue;
                        }

                        foreach (int n in list)
                        {
                            var np = _grid.nodes[n].position;
                            if (Mathf.Abs(px - np.x) <= half
                                && Mathf.Abs(pz - np.y) <= half)
                            {
                                hit.Add(n);
                            }
                        }
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
                        float px = corner.x + d * (i % across),
                            pz = corner.z + d * (i / across);
                        if (Mathf.Abs(px - np.x) <= half
                            && Mathf.Abs(pz - np.y) <= half)
                        {
                            expected.Add(n);
                            break;
                        }
                    }
                }

                Checked++;
                if (!expected.SequenceEqual(hit))
                {
                    Mismatches++;
                    if (Mismatches <= 5)
                    {
                        Plugin.Log.LogWarning(
                            $"LoadSpeed: {GO.name}: nodes "
                                + $"{string.Join(",", hit)}, the game's search "
                                + $"{string.Join(",", expected)}");
                    }
                }
            }

            foreach (int n in hit)
            {
                var node = _grid.nodes[n];
                // a new Cullable is never already listed
                node.cullables.Add(cullable);
                if (!co.nodes.Contains(node))
                {
                    co.nodes.Add(node);
                }
            }

            return false;
        }

        /// <summary>
        /// The same as WorldGrid.registerToClosestNode: the first node at the
        /// smallest distance (under 10,000), with the distance worked out in
        /// place instead of through Core.trueDistance's conversions.
        /// </summary>
        internal bool RegisterToClosestNode(WorldGrid w, GameObject GO)
        {
            WorldGrid.Grid currentGrid = w.currentGrid;
            List<WorldGrid.Node> nodes = null;
            if (currentGrid != null)
            {
                nodes = currentGrid.nodes;
            }

            if (nodes == null || nodes.Count == 0)
            {
                return true;
            }

            Vector3 pos = GO.transform.position;
            var at = new Vector2(pos.x, pos.z);
            int index = 0;
            float best = 10000f;
            for (int i = 0; i < nodes.Count; i++)
            {
                float dist = Vector2.Distance(at, nodes[i].position);
                if (dist < best)
                {
                    best = dist;
                    index = i;
                }
            }

            nodes[index].cullables.Add(new WorldGrid.Cullable(GO));
            return false;
        }

        /// <summary>
        /// The same as WorldGrid.registerToNode, with its two searches looked
        /// up instead.
        /// </summary>
        internal bool RegisterToNode(
            WorldGrid w, GameObject GO, WorldGrid.Cullable.Type type)
        {
            if (w.currentGrid == null)
            {
                return true;
            }

            if (!ReferenceEquals(w.currentGrid, _grid))
            {
                Index(w);
            }

            var co = CullableOf(GO);
            if (co.registered && co.node != null)
            {
                co.node.removeCullable(GO);
            }

            co.registered = true;
            if (GO.GetComponent<Location>() != null)
            {
                return false;
            }

            var cullable = new WorldGrid.Cullable(GO, type);
            if (GO.GetComponent<MapElement>() != null)
            {
                cullable.isMapElement = true;
            }

            if (_listed.Add(co) && !_grid.cullables.Contains(co))
            {
                _grid.cullables.Add(co);
            }

            // The first node (in the grid's order) whose square, distance/2
            // each way, holds the object.
            float half = w.distance / 2;
            Vector3 pos = GO.transform.position;
            int best = int.MaxValue;
            int x0 = Mathf.FloorToInt((pos.x - half) / _cell),
                x1 = Mathf.FloorToInt((pos.x + half) / _cell);
            int z0 = Mathf.FloorToInt((pos.z - half) / _cell),
                z1 = Mathf.FloorToInt((pos.z + half) / _cell);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out var list))
                    {
                        continue;
                    }

                    foreach (int i in list)
                    {
                        if (i >= best)
                        {
                            continue;
                        }

                        var np = _grid.nodes[i].position;
                        if (Mathf.Abs(pos.x - np.x) <= half
                            && Mathf.Abs(pos.z - np.y) <= half)
                        {
                            best = i;
                        }
                    }
                }
            }

            if (Verify)
            {
                int expected = int.MaxValue;
                for (int i = 0; i < _grid.nodes.Count; i++)
                {
                    var np = _grid.nodes[i].position;
                    if (Mathf.Abs(pos.x - np.x) <= half
                        && Mathf.Abs(pos.z - np.y) <= half)
                    {
                        expected = i;
                        break;
                    }
                }

                Checked++;
                if (expected != best)
                {
                    Mismatches++;
                    if (Mismatches <= 5)
                    {
                        Plugin.Log.LogWarning(
                            $"LoadSpeed: {GO.name} at {pos}: node {best}, the "
                                + $"game's search {expected}");
                    }
                }
            }

            if (best != int.MaxValue)
            {
                var node = _grid.nodes[best];
                // a new Cullable is never already listed (it has no Equals of
                // its own)
                node.cullables.Add(cullable);
                co.node = node;
                return false;
            }

            w.registerToClosestNode(GO);
            return false;
        }
    }
}
