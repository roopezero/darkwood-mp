using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// While a client plays the host's world, every save path the game uses
    /// points into that world's folder in the mod's cache instead of the
    /// player's own save folder (1_4Save). Every save path in SaveManager is
    /// built from baseSaveDirectory, so redirecting that one getter keeps the
    /// player's own saves untouched, whatever the game reads or writes while in
    /// the host's world.
    ///
    /// The redirect lasts until the main menu loads again after the world was
    /// started. Its state is ModHost's SaveFolders; this class keeps no
    /// state, so its many callers read it as before.
    /// </summary>
    [HarmonyPatch(
        typeof(SaveManager), nameof(SaveManager.baseSaveDirectory),
        MethodType.Getter)]
    public static class SaveRedirect
    {
        private static SaveFolders Folders => Plugin.Instance.Host.SaveFolders;

        /// <summary>
        /// The folder standing in for 1_4Save, or null for the player's own
        /// saves.
        /// </summary>
        public static string Dir => Folders.Dir;

        public static bool Active => Folders.Active;

        public static void BeginTemporary(string dir) =>
            Folders.BeginTemporary(dir);

        public static void EndTemporary() => Folders.EndTemporary();

        /// <summary>
        /// Point the game's saves at dir; call right before starting the world.
        /// </summary>
        public static void Begin(string dir) => Folders.Begin(dir);

        /// <summary>
        /// Saves go to your own folder again (a test that pointed them at a
        /// copy and started no world).
        /// </summary>
        public static void End() => Folders.End();

        private static bool Prefix(ref string __result)
        {
            string dir = Folders.Now;
            if (dir == null)
            {
                return true;
            }

            __result = dir;
            return false;
        }
    }
}
