using System;
using System.Collections.Generic;
using HarmonyLib;
using Pathfinding;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Every 0.3 s the game marks the pathfinding nodes under each player light as lit (Controller.updateLogicLights
    /// -> Light2D.lightGraphNodes -> AstarPath.GetNodesAround). GetNodesAround works out node indices from the light's
    /// position in the nearest grid and only handles a light inside the world's grids or at their edge: one further out
    /// reads past a grid's node array and logs an IndexOutOfRangeException each time. The game's own lights are always
    /// in the grid while it runs this (it skips the update in a dream). After a level dream shared with the others
    /// (World.DreamSync) ends, the dream's level stays in the world ("OutsideLocations/&lt;dream&gt;_done") with its wall
    /// lamps on, about 80,000 units off the grid, and the map test logged 260 of these exceptions from them. A
    /// puppet's torch where this game has no grid would do the same. Such a light has no nodes to light, so this skips
    /// the node lookup for it and clears the nodes it held, rather than let the game index outside the grid.
    /// </summary>
    [HarmonyPatch(typeof(Light2D), nameof(Light2D.populateAffectedNodes), new[] { typeof(int) })]
    static class LightGridPatch
    {
        private static readonly HashSet<int> Logged = new HashSet<int>();

        static bool Prefix(Light2D __instance, int _nodeLightRadius)
        {
            try
            {
                var astar = AstarPath.active;
                if (astar == null) return true;
                var g = astar.getNearestGraphFast(__instance.transform.position);
                if (g == null || g.nodes == null || g.nodes.Length == 0 || g.nodeSize <= 0f) return true; // the game returns at once
                Vector3 pos = __instance.transform.position;
                Vector3 d = pos - (Vector3)g.nodes[0].position;
                int x = Mathf.RoundToInt(d.x / g.nodeSize), z = Mathf.RoundToInt(d.z / g.nodeSize);
                int r = _nodeLightRadius;
                if (x >= -r && x < g.Width + r && z >= -r && z < g.Depth + r) return true;
                var nodes = __instance.affectedNodes;
                if (nodes != null) Array.Clear(nodes, 0, nodes.Length);
                if (Logged.Add(__instance.GetInstanceID()))
                    Plugin.Log.LogInfo($"[lights] {Path(__instance.transform)} at {pos} is outside the pathfinding grid: it lights no nodes there");
                return false;
            }
            catch { return true; }
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
