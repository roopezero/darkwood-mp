using HarmonyLib;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// Runs with or without a session (LightGrid.Populates).
    /// </summary>
    [HarmonyPatch(
        typeof(Light2D), nameof(Light2D.populateAffectedNodes),
        new[] { typeof(int) })]
    static class Light2DPopulateAffectedNodes
    {
        static bool Prefix(Light2D __instance, int _nodeLightRadius)
        {
            return Plugin.Instance.Host.Lights.Populates(
                __instance, _nodeLightRadius);
        }
    }
}
