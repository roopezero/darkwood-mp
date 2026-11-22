using HarmonyLib;

namespace DarkwoodMP.World
{
    // The save book's patches also run in a game played alone: a new game or
    // a deleted slot drops the slot's record either way (SaveBook).

    /// <summary>
    /// A new game starts in a slot: its record starts empty
    /// (SaveBook.OnNewGame).
    /// </summary>
    [HarmonyPatch(typeof(UI), nameof(UI.initNewGame))]
    static class UIInitNewGame
    {
        static void Prefix() => SaveBook.OnNewGame();
    }

    /// <summary>
    /// The game deletes a slot's save: its record and the host's characters
    /// for its joiners go too (SaveBook.OnSlotDeleted).
    /// </summary>
    [HarmonyPatch(
        typeof(SaveManager), nameof(SaveManager.deleteCurrentProfile))]
    static class SaveManagerDeleteCurrentProfile
    {
        static void Prefix(SaveManager __instance) =>
            SaveBook.OnSlotDeleted(__instance);
    }
}
