using IO = System.IO;

namespace DarkwoodMP.World.Saves
{
    /// Where the game's saves go while the mod plays a world that isn't the
    /// player's own (SaveRedirect, WorldShare, SaveBook). Every path the game
    /// saves to is built from one folder, so choosing it keeps the player's
    /// own saves out of reach.
    public static class RedirectRules
    {
        /// The main menu's scene (Core.returnToMainMenu loads it).
        public const string MenuScene = "Darkwood";

        public const string BookName = "DarkwoodMP.saves.json";
        public const string RedirectedBookName = "darkwoodmp-saves.json";

        /// The save folder the game gets (SaveFolders.Now): the
        /// snapshot's for its one frame, else the redirect's, else null for
        /// the game's own. An empty temporary folder still wins: only null
        /// passes it by.
        public static string SaveFolder(string temporary, string redirect)
        {
            if (temporary != null)
            {
                return temporary;
            }

            return redirect;
        }

        /// A scene load ends the redirect (SaveFolders.OnSceneLoaded): a
        /// Single load of the main menu after a world was started.
        public static bool EndsAt(bool started, string scene, bool single) =>
            started && scene == MenuScene && single;

        /// The redirect works (WorldShare.RedirectWorks): the world's save
        /// path is inside the folder. The save path is SaveManager's
        /// dynamicFile with a current profile, else its localProfilesFile;
        /// null when there is no SaveManager, which doesn't work.
        public static bool Works(string save, string folder, bool ignoreCase) =>
            DeleteRules.IsUnder(save, folder, ignoreCase);

        /// We play a copy of a host's world (WorldShare.PlayingHostCopy):
        /// saves are redirected into the worlds cache.
        public static bool PlayingHostCopy(
            string redirect,
            string cache,
            bool ignoreCase) =>
            redirect != null
                && DeleteRules.IsUnder(redirect, cache, ignoreCase);

        /// The save book's file (SaveBook.FilePath): the config folder's
        /// when saves aren't redirected; none in a host's copy, or when that
        /// isn't known (hostCopy null: no WorldShare); else beside the
        /// redirected saves.
        public static string BookFile(
            string config,
            string redirect,
            bool? hostCopy)
        {
            if (redirect == null)
            {
                return IO.Path.Combine(config, BookName);
            }

            if (hostCopy != false)
            {
                return null;
            }

            return IO.Path.Combine(redirect, RedirectedBookName);
        }

        /// A new game in a slot drops what the mod kept for the slot's last
        /// world (SaveBook.OnNewGame): not when the slot's world is the
        /// one playing.
        public static bool NewGameForgets(bool profile, bool active) =>
            profile && !active;

        /// A new game also deletes the slot's players' folder: with a
        /// SaveManager, and never in a host's copy. Unknown (null) deletes.
        public static bool NewGameDeletes(bool saves, bool? hostCopy) =>
            saves && hostCopy != true;

        /// A deleted slot drops and deletes the same (SaveBook.OnSlotDeleted):
        /// with a profile, never in a host's copy. Unknown (null) does.
        public static bool SlotDeleteForgets(bool profile, bool? hostCopy) =>
            profile && hostCopy != true;
    }
}
