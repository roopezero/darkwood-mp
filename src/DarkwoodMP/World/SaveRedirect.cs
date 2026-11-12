using HarmonyLib;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// While a client plays the host's world, every save path the game uses points into that world's folder in
    /// the mod's cache instead of the player's own save folder (1_4Save). Every save path in SaveManager is built
    /// from baseSaveDirectory, so redirecting that one getter keeps the player's own saves untouched, whatever
    /// the game reads or writes while in the host's world.
    ///
    /// The redirect lasts until the main menu loads again after the world was started.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.baseSaveDirectory), MethodType.Getter)]
    public static class SaveRedirect
    {
        /// <summary>The folder standing in for 1_4Save, or null for the player's own saves.</summary>
        public static string Dir { get; private set; }
        private static bool _worldStarted;
        /// <summary>A folder saves go to for a moment (the host saving a snapshot for a joiner), over Dir.</summary>
        private static string _temporary;

        public static bool Active => Dir != null;

        public static void BeginTemporary(string dir) => _temporary = dir;
        public static void EndTemporary() => _temporary = null;

        public static void Init() => SceneManager.sceneLoaded += OnSceneLoaded;

        /// <summary>Point the game's saves at dir; call right before starting the world.</summary>
        public static void Begin(string dir)
        {
            Dir = dir;
            _worldStarted = true;
            Plugin.Log.LogInfo($"Saves now go to the host's world in {dir}");
        }

        /// <summary>Saves go to your own folder again (a test that pointed them at a copy and started no world).</summary>
        public static void End()
        {
            Dir = null;
            _worldStarted = false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // "Darkwood" is the main menu's scene (Core.returnToMainMenu).
            if (_worldStarted && scene.name == "Darkwood" && mode == LoadSceneMode.Single)
            {
                Plugin.Log.LogInfo("Back at the main menu: saves go to your own save folder again");
                Dir = null;
                _worldStarted = false;
                // The menu's profile list still holds the host's profile; drop it so it reloads from 1_4Save.
                Core.profiles.Clear();
                Core.currentProfile = null;
            }
        }

        private static bool Prefix(ref string __result)
        {
            string dir = _temporary ?? Dir;
            if (dir == null) return true;
            __result = dir;
            return false;
        }
    }
}
