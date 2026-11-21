using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The workbench's storage was drawn: it's drawn smaller if it would run
    /// off the screen (WorkbenchStorage.FitToScreen). Low priority, so it runs
    /// after the Sort &amp; Stack button is added. Also without co-op.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.show))]
    [HarmonyPriority(Priority.Low)]
    static class InventoryShow
    {
        static void Postfix(Inventory __instance)
        {
            WorkbenchStorage.FitToScreen(__instance);
        }
    }

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
