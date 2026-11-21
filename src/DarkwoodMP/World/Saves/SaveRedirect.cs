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
    /// started. The getter's patch is SaveManagerBaseSaveDirectory, which
    /// also runs in a game played alone (a test's copy of the saves). Its
    /// state is ModHost's SaveFolders, which callers use directly; this class
    /// keeps only the getter patch's step.
    /// </summary>
    public static class SaveRedirect
    {
        /// <summary>
        /// SaveManager.baseSaveDirectory: sets folder to where the saves go
        /// now and is true, or false for the game's own folder, left as it
        /// is.
        /// </summary>
        internal static bool Replace(ref string folder)
        {
            string dir = Plugin.Instance.Host.SaveFolders.Now;
            if (dir == null)
            {
                return false;
            }

            folder = dir;
            return true;
        }
    }
}
