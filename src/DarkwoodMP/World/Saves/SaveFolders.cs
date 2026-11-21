using DarkwoodMP.World.Saves;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Where the game's saves go (SaveRedirect): the folder standing in for
    /// the player's own (1_4Save) while a client plays the host's world or a
    /// test plays a copy, and a folder for a moment while the host saves a
    /// snapshot for a joiner. It lives as long as the game (ModHost): the
    /// redirect outlasts a session, until the main menu loads again after
    /// the world was started.
    /// </summary>
    internal sealed class SaveFolders
    {
        /// <summary>
        /// The folder standing in for 1_4Save, or null for the player's own
        /// saves.
        /// </summary>
        public string Dir { get; private set; }

        private bool _worldStarted;

        /// <summary>
        /// A folder saves go to for a moment (the host saving a snapshot for a
        /// joiner), over Dir.
        /// </summary>
        private string _temporary;

        public SaveFolders()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public bool Active => Dir != null;

        /// <summary>
        /// The folder the game's saves go to now, or null for its own.
        /// </summary>
        public string Now => RedirectRules.SaveFolder(_temporary, Dir);

        public void BeginTemporary(string dir) => _temporary = dir;

        public void EndTemporary() => _temporary = null;

        /// <summary>
        /// Point the game's saves at dir; call right before starting the world.
        /// </summary>
        public void Begin(string dir)
        {
            Dir = dir;
            _worldStarted = true;
            Plugin.Log.LogInfo($"Saves now go to the host's world in {dir}");
        }

        /// <summary>
        /// Saves go to your own folder again (a test that pointed them at a
        /// copy and started no world).
        /// </summary>
        public void End()
        {
            Dir = null;
            _worldStarted = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // the main menu's scene loaded after the world was started
            if (RedirectRules.EndsAt(
                _worldStarted, scene.name, mode == LoadSceneMode.Single))
            {
                Plugin.Log.LogInfo(
                    "Back at the main menu: saves go to your own save folder "
                        + "again");
                Dir = null;
                _worldStarted = false;
                // The menu's profile list still holds the host's profile; drop
                // it so it reloads from 1_4Save.
                Core.profiles.Clear();
                Core.currentProfile = null;
            }
        }
    }
}
