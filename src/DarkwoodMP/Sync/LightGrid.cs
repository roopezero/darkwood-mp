using System;
using System.Collections.Generic;
using DarkwoodMP.Sync.Players;
using HarmonyLib;
using Pathfinding;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Every 0.3 s the game marks the pathfinding nodes under each player light
    /// as lit (Controller.updateLogicLights -> Light2D.lightGraphNodes ->
    /// AstarPath.GetNodesAround). GetNodesAround works out node indices from
    /// the light's position in the nearest grid and only handles a light inside
    /// the world's grids or at their edge: one further out reads past a grid's
    /// node array and logs an IndexOutOfRangeException each time. The game's
    /// own lights are always in the grid while it runs this (it skips the
    /// update in a dream). After a level dream shared with the others
    /// (World.DreamSync) ends, the dream's level stays in the world
    /// ("OutsideLocations/&lt;dream&gt;_done") with its wall lamps on, about
    /// 80,000 units off the grid, and the map test logged 260 of these
    /// exceptions from them. A puppet's torch where this game has no grid would
    /// do the same. Such a light has no nodes to light, so this skips the node
    /// lookup for it and clears the nodes it held, rather than let the game
    /// index outside the grid.
    /// </summary>
    public sealed class LightGrid
    {
        // The lights already logged, by instance id.
        private readonly HashSet<int> _logged = new HashSet<int>();

        /// <summary>
        /// Light2D.populateAffectedNodes: false skips the game's lookup for
        /// a light outside the grid, after clearing its nodes.
        /// </summary>
        public bool Populates(Light2D light, int radius)
        {
            try
            {
                Vector3 pos = light.transform.position;
                if (!LightGridRules.SkipsLookup(
                    FactsOf(pos, radius)))
                {
                    return true;
                }

                var nodes = light.affectedNodes;
                if (nodes != null)
                {
                    Array.Clear(nodes, 0, nodes.Length);
                }

                if (_logged.Add(light.GetInstanceID()))
                {
                    Plugin.Log.LogInfo(
                        $"[lights] {Path(light.transform)} at {pos} is "
                            + "outside the pathfinding grid: it lights no "
                            + "nodes there");
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        // Without a grid or nodes the game returns at once.
        private static LightGridFacts FactsOf(Vector3 pos, int radius)
        {
            var astar = AstarPath.active;
            if (astar == null)
            {
                return default;
            }

            var g = astar.getNearestGraphFast(pos);
            if (g == null || g.nodes == null)
            {
                return new LightGridFacts { Pathfinding = true };
            }

            Vec3 origin = default;
            if (g.nodes.Length > 0)
            {
                var first = (Vector3)g.nodes[0].position;
                origin = new Vec3(first.x, first.y, first.z);
            }

            return new LightGridFacts
            {
                Pathfinding = true,
                Graph = true,
                Nodes = g.nodes.Length,
                NodeSize = g.nodeSize,
                Width = g.Width,
                Depth = g.Depth,
                Light = new Vec3(pos.x, pos.y, pos.z),
                Origin = origin,
                Radius = radius,
            };
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p != null; p = p.parent)
            {
                s = p.name + "/" + s;
            }

            return s;
        }
    }

    /// <summary>
    /// Runs with or without a session (LightGrid.Populates).
    /// </summary>
    [HarmonyPatch(
        typeof(Light2D), nameof(Light2D.populateAffectedNodes),
        new[] { typeof(int) })]
    static class LightGridPatch
    {
        static bool Prefix(Light2D __instance, int _nodeLightRadius)
        {
            return Plugin.Instance.Host.Lights.Populates(
                __instance, _nodeLightRadius);
        }
    }
}
