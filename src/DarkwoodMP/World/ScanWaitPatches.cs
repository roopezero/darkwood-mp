using HarmonyLib;
using Pathfinding;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A scan first finishes the pending graph updates (ScanWait). It runs
    /// in a game played alone too: the game hits the same race there.
    /// </summary>
    [HarmonyPatch(
        typeof(AstarPath), nameof(AstarPath.ScanLoop),
        new[] { typeof(OnScanStatus), typeof(int) })]
    static class AstarPathScanLoop
    {
        static void Prefix(AstarPath __instance) =>
            ScanWait.BeforeScan(__instance);
    }
}
