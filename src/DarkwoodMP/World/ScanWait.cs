using HarmonyLib;
using Pathfinding;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A door or window that opens, closes or gets boarded up changes the pathfinding graph (GraphUpdateScene.Apply),
    /// and the A* library then recounts the connected areas of every graph (AstarPath.FloodFill) on its own thread.
    /// When a location loads, the game scans that location's graph on the main thread one frame later
    /// (OutsideLocations.spawnLocation, AstarPath.ScanLoop), and the host scans the ones it loads for the others the
    /// same way (Locations.ScanPathfinding). ScanLoop in this version of the library stops the path threads but not
    /// the graph update thread, and a grid scan first makes the graph's node array and then fills it, so a flood fill
    /// that runs at that moment meets empty slots and logs "Exception while updating graphs: NullReferenceException
    /// ... AstarPath.FloodFill" and stops, leaving the areas half counted. The map test hit it when a level dream
    /// began: the dream's doors and windows set their graph updates as it loaded, and its graph was scanned while the
    /// flood fill they queued was still running. The game can do the same in single player; more doors move in co-op.
    /// AstarData.RemoveGraph already waits for the pending graph work first; this makes ScanLoop do the same.
    /// </summary>
    [HarmonyPatch(typeof(AstarPath), nameof(AstarPath.ScanLoop), new[] { typeof(OnScanStatus), typeof(int) })]
    static class ScanWaitPatch
    {
        private static readonly AccessTools.FieldRef<AstarPath, bool> ProcessingWorkItems = AccessTools.FieldRefAccess<AstarPath, bool>("processingWorkItems");
        private static readonly AccessTools.FieldRef<AstarPath, bool> WorkItemsQueued = AccessTools.FieldRefAccess<AstarPath, bool>("workItemsQueued");

        static void Prefix(AstarPath __instance)
        {
            try
            {
                if (!Application.isPlaying || __instance == null || __instance.graphs == null || __instance.isScanning)
                {
                    return;
                }
                if (ProcessingWorkItems(__instance))
                {
                    return; // called from inside a work item: flushing would recurse
                }
                bool pending = WorkItemsQueued(__instance) || __instance.IsAnyGraphUpdatesQueued;
                if (!pending)
                {
                    return;
                }
                // Hand queued graph updates over, then finish every work item, waiting for the graph update thread.
                __instance.FlushGraphUpdates();
                __instance.FlushWorkItems();
                Plugin.Log.LogInfo("[pathfinding] finished the pending graph updates before a scan");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[pathfinding] couldn't finish the graph updates before a scan: {e.Message}"); }
        }
    }
}
