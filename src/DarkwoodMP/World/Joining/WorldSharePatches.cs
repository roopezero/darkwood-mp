namespace DarkwoodMP.World
{
    /// <summary>
    /// Going back to the main menu ends the session: the host's stops its
    /// server (WorldShare.HostToMainMenu), a joiner's leaves the host's game
    /// (WorldShare.ClientToMainMenu). The game only goes back to the main menu
    /// when its player chose to (the pause menu, the permadeath screen, the
    /// credits); the mod's own reloads and chapter changes load the next world
    /// without it.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Core), nameof(Core.returnToMainMenu))]
    static class CoreReturnToMainMenu
    {
        static void Prefix()
        {
            if (Coop.Current == null)
            {
                return;
            }

            WorldShare share = Plugin.Instance.Host.Share;
            if (share != null)
            {
                share.ReturnToMainMenu();
            }
        }
    }
}
