using HarmonyLib;

namespace DarkwoodMP
{
    // MenuEntry's, MenuLayout's and PauseHud's patches. They run playing
    // alone too: the menus look the same with no session.

    [HarmonyPatch(typeof(MainMenu), "OnEnable")]
    static class MainMenuOnEnable
    {
        static void Postfix(MainMenu __instance) =>
            Plugin.Instance.Host.MenuEntry.PauseMenuOpened(__instance);
    }

    /// <summary>
    /// The game placed one of the menu's things (PositionMe): lay the menu out
    /// again at once, so it never shows in the game's place.
    /// </summary>
    [HarmonyPatch(typeof(PositionMe), nameof(PositionMe.init))]
    static class PositionMeInit
    {
        static void Postfix(PositionMe __instance) =>
            Plugin.Instance.Host.MenuLayout.Placed(__instance);
    }

    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.close))]
    static class MainMenuClose
    {
        static void Postfix(MainMenu __instance) =>
            Plugin.Instance.Host.MenuEntry.PauseMenuClosed(__instance);
    }

    /// <summary>
    /// The menu went away without close (the game turned it off): the HUD comes
    /// back too.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), "OnDisable")]
    static class MainMenuOnDisable
    {
        static void Postfix() => Plugin.Instance.Host.PauseHud.Restore();
    }
}
