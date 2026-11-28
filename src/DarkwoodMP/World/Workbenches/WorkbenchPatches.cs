using HarmonyLib;

namespace DarkwoodMP.World.Workbenches
{
    /// <summary>
    /// A workbench opened: its storage gets the current level's rows
    /// (WorkbenchStorage.Opened). Also without co-op.
    /// </summary>
    [HarmonyPatch(typeof(Workbench), nameof(Workbench.open))]
    static class WorkbenchOpen
    {
        static void Prefix(Workbench __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || plugin.Host == null)
            {
                return;
            }

            plugin.Host.Workbenches.Opened(__instance);
        }
    }
}
