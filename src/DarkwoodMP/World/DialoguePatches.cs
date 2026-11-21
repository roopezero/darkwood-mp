using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A trade opened: DialogueGuard keeps the trader's window.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.openTrade))]
    static class DialogueWindowOpenTrade
    {
        static void Postfix(DialogueWindow __instance) =>
            DialogueGuard.AfterOpenTrade(__instance);
    }

    /// <summary>
    /// The trade window's close button and Escape (escPress): closeTrade
    /// throws without the trader.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.closeTrade))]
    static class DialogueWindowCloseTrade
    {
        static bool Prefix(DialogueWindow __instance) =>
            DialogueGuard.BeforeCloseTrade(__instance);
    }

    /// <summary>close() does nothing once the NPC is destroyed.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.close))]
    static class DialogueWindowClose
    {
        static bool Prefix(DialogueWindow __instance) =>
            DialogueGuard.BeforeClose(__instance);
    }

    /// <summary>
    /// Escape: a line being written (onInstantClick) and the other menus use
    /// the NPC too.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.escPress))]
    static class DialogueWindowEscPress
    {
        static bool Prefix(DialogueWindow __instance) =>
            DialogueGuard.BeforeClose(__instance);
    }

    /// <summary>
    /// A line still being written when the dialogue closes: skip the
    /// game's finishedElement on the closed window.
    /// </summary>
    [HarmonyPatch(
        typeof(DialogueWindow), nameof(DialogueWindow.finishedElement))]
    static class DialogueWindowFinishedElement
    {
        static bool Prefix(DialogueWindow __instance) =>
            DialogueGuard.BeforeFinishedLine(__instance);
    }
}
