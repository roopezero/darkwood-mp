using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A trader stocking up (appearing, a new day): the host's game does it
    /// and sends it; a client's asks (TraderSync).
    /// </summary>
    [HarmonyPatch(typeof(NPC), "randomizeTraderInv")]
    static class RandomizeTraderInv
    {
        static bool Prefix(NPC __instance) =>
            TraderSync.BeforeStocking(__instance);

        static void Postfix(NPC __instance) =>
            TraderSync.AfterStocking(__instance);
    }

    /// <summary>
    /// An accepted trade (DialogueWindow.acceptTrade moves what's in the two
    /// exchange windows to the trader and to the player): share the
    /// trader's new stock (TraderSync).
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.acceptTrade))]
    static class AcceptTrade
    {
        static void Prefix(
            DialogueWindow __instance, out TradeWindows __state) =>
            __state = TraderSync.BeforeTrade(__instance);

        static void Postfix(DialogueWindow __instance, TradeWindows __state) =>
            TraderSync.AfterTrade(__instance, __state);
    }
}
