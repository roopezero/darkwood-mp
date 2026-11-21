using HarmonyLib;

namespace DarkwoodMP.World
{
    // SharedIds' patches. They run with no session too: the host loads and
    // saves its world before it hosts.
    //
    // SaveManager.SaveState's own methods are one line each, and Mono inlines
    // them, so a patch on them never runs. The counter is read around them
    // instead.

    /// <summary>
    /// The counter as a save writes it: SaveManager.Save gives out the new ids,
    /// then writes the counter, all before it returns.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[]
    {
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    static class SaveManagerSave
    {
        static void Postfix(SaveManager __instance) =>
            SharedIds.Saved(__instance);
    }

    /// <summary>
    /// The counter as a loaded save sets it: the loading coroutine sets it
    /// (SaveState.loadValues) just before it loads the player
    /// (Player.SaveState.loadValues). Characters.cs calls that too, outside
    /// loading.
    /// </summary>
    [HarmonyPatch(
        typeof(Player.SaveState), nameof(Player.SaveState.loadValues),
        new[] { typeof(bool) })]
    static class PlayerSaveStateLoadValues
    {
        static void Prefix(bool onlyState) =>
            SharedIds.LoadingPlayer(onlyState);
    }
}
