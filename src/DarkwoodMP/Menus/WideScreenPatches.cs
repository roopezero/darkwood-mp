using HarmonyLib;

namespace DarkwoodMP.Menus
{
    // The screen's zoom and size matter alone as much as in co-op, so these
    // run with no session too.

    /// <summary>
    /// The first start on an ultrawide monitor (WideScreen.FirstStart).
    /// </summary>
    [HarmonyPatch(typeof(Core), "machineSettingsInit")]
    static class CoreMachineSettingsInit
    {
        static void Postfix() => WideScreen.FirstStart();
    }

    /// <summary>
    /// The camera's zoom, by height on wide screens (WideScreen.Apply).
    /// </summary>
    [HarmonyPatch(typeof(Controller), nameof(Controller.refreshZoom))]
    static class ControllerRefreshZoom
    {
        static void Postfix(Controller __instance) =>
            WideScreen.Apply(__instance);
    }
}
