using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Every save path in SaveManager is built from baseSaveDirectory, so
    /// this one getter points the game's saves away from the player's own
    /// (SaveRedirect). It runs with no session too: a client's redirect
    /// lasts until the main menu, and a test's copy of the saves is played
    /// alone.
    /// </summary>
    [HarmonyPatch(
        typeof(SaveManager), nameof(SaveManager.baseSaveDirectory),
        MethodType.Getter)]
    static class SaveManagerBaseSaveDirectory
    {
        static bool Prefix(ref string __result) =>
            !SaveRedirect.Replace(ref __result);
    }
}
