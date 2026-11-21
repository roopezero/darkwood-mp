using HarmonyLib;

namespace DarkwoodMP.World
{
    // A world is left (WorldTeardown) by the game's own calls that load the
    // next scene. These run in a game played alone too: the game's teardown
    // throws there as well.

    [HarmonyPatch(typeof(Core), nameof(Core.returnToMainMenu))]
    static class CoreReturnToMainMenu
    {
        static void Prefix() => Plugin.Instance.Host.Teardown.Begin();
    }

    [HarmonyPatch(typeof(Core), nameof(Core.restartGame))]
    static class CoreRestartGame
    {
        static void Prefix() => Plugin.Instance.Host.Teardown.Begin();
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.generateChapter))]
    static class ControllerGenerateChapter
    {
        static void Prefix() => Plugin.Instance.Host.Teardown.Begin();
    }

    /// <summary>
    /// A character destroyed with its world skips the parts of OnDestroy
    /// that reach into the world (WorldTeardown.RunsOnDestroy).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.OnDestroy))]
    static class CharacterOnDestroy
    {
        static bool Prefix(Character __instance) =>
            Plugin.Instance.Host.Teardown.RunsOnDestroy(__instance);
    }
}
