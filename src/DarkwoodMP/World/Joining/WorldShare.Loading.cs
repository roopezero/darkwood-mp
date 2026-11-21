using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World.Joining;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Client: loading the downloaded world, and starting a profile.
    /// </summary>
    public sealed partial class WorldShare
    {
        // ---- loading the world ----------------------------------------------

        private void OnWelcomed()
        {
            if (_session.IsHost || _readyHash == null)
            {
                return;
            }

            string dir = Path.Combine(_cacheDir, WorldFiles.Hex(_readyHash));
            // already up if we downloaded the world; this is the first sign if
            // we had it
            JoinScreen.Begin();
            _runner.StartCoroutine(LoadWorld(dir));
        }

        private IEnumerator LoadWorld(string worldDir)
        {
            GameProfile profile;
            try
            {
                profile = JsonConvert.DeserializeObject<GameProfile>(
                    File.ReadAllText(Path.Combine(worldDir, "profile.json")));
            }
            catch (Exception e)
            {
                LoadStatus = $"Couldn't read the host's world: {e.Message}";
                Plugin.Log.LogError(LoadStatus);
                JoinScreen.Fail(LoadStatus);
                yield break;
            }

            if (!Core.mainMenu || PauseMenuInWorld)
            {
                if (Core.mainMenu)
                {
                    Singleton<MainMenu>.Instance.close();
                }

                LoadStatus = "Leaving your game to join the host's world...";
                Plugin.Log.LogInfo(LoadStatus);
                // On our way into the host's world (a WorldReload, a chapter):
                // the session stays (ClientToMainMenu).
                _joiningWorld = true;
                try
                {
                    Core.returnToMainMenu();
                }
                finally
                {
                    _joiningWorld = false;
                }
            }

            // waiting for the main menu first
            LoadStatus = $"Loading the host's world (day {profile.day})...";
            float until = Time.realtimeSinceStartup + 120f;
            while (!MainMenuReady())
            {
                if (Time.realtimeSinceStartup > until)
                {
                    LoadStatus = "Timed out waiting for the main menu.";
                    JoinScreen.Fail(LoadStatus);
                    yield break;
                }

                if (!_session.Connected)
                {
                    LoadStatus = null;
                    yield break;
                }

                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.5f);

            LoadStatus = $"Loading the host's world (day {profile.day})...";
            Plugin.Log.LogInfo(LoadStatus);
            if (!StartProfile(profile, worldDir))
            {
                LoadStatus =
                    "Couldn't keep the host's world out of your own saves, so "
                        + "it wasn't loaded (see the log).";
                JoinScreen.Fail(LoadStatus);
                INetBackend backend = _session.Backend;
                if (backend != null)
                {
                    backend.Leave();
                }

                yield break;
            }

            JoinScreen.HandOver();
        }

        public static bool MainMenuReady()
        {
            var mm = Singleton<MainMenu>.Instance;
            return Core.mainMenu && !PauseMenuInWorld && mm != null
                && mm.Menu0 != null && mm.Menu0.activeInHierarchy
                && Singleton<UI>.Instance != null;
        }

        /// <summary>
        /// Start a profile from the main menu as its Play button does, with
        /// saves in saveDir (null: your own). False, and nothing started, if
        /// the game would still save somewhere else.
        /// </summary>
        public static bool StartProfile(GameProfile profile, string saveDir)
        {
            if (saveDir != null)
            {
                SaveRedirect.Begin(saveDir);
            }

            Core.currentProfile = profile;
            if (saveDir != null && !RedirectWorks(saveDir))
            {
                Core.currentProfile = null;
                SaveRedirect.End();
                return false;
            }

            // The menu always has five profile slots; put this one in its own.
            var slots = new List<GameProfile>();
            for (int i = 1; i <= Math.Max(5, profile.id); i++)
            {
                GameProfile item;
                if (i == profile.id)
                {
                    item = profile;
                }
                else
                {
                    item = new GameProfile(i, _Active: false, 0);
                }

                slots.Add(item);
            }

            Core.profiles = slots;
            Singleton<UI>.Instance.StartCoroutine(
                Singleton<UI>.Instance.initLoadGame());
            return true;
        }

        /// <summary>
        /// Whether the game's saves really go under saveDir now that
        /// SaveRedirect points there: the profile list's path and, with
        /// Core.currentProfile set, the world's save path, as the game builds
        /// them. If baseSaveDirectory was inlined somewhere before the redirect
        /// was patched in, it would show here; the caller then doesn't load,
        /// rather than save into the player's own saves, and ends the redirect.
        /// </summary>
        internal static bool RedirectWorks(string saveDir)
        {
            var sm = Singleton<SaveManager>.Instance;
            string save;
            if (Core.currentProfile != null)
            {
                if (sm != null)
                {
                    save = sm.dynamicFile;
                }
                else
                {
                    save = null;
                }
            }
            else
            {
                if (sm != null)
                {
                    save = sm.localProfilesFile;
                }
                else
                {
                    save = null;
                }
            }

            if (WorldFiles.IsUnder(save, saveDir))
            {
                return true;
            }

            Plugin.Log.LogError(
                "The save redirect isn't working: the world would save to "
                    + $"{save}, not under {saveDir}");
            return false;
        }

        /// <summary>
        /// "--dwmp-continue N": load your own profile N at start (for testing
        /// and hosting).
        /// </summary>
        public IEnumerator ContinueOwnProfile(int id, string sandboxDir = null)
        {
            while (!MainMenuReady())
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(1f);
            // "--dwmp-sandbox DIR": play a copy of the saves in DIR (laid out
            // as 1_4Save), never your own.
            if (sandboxDir != null)
            {
                SaveRedirect.Begin(sandboxDir);
            }

            var state = Singleton<SaveManager>.Instance.loadGameProfiles();
            GameProfile profile = null;
            if (state != null)
            {
                List<GameProfile> profiles = state.profiles;
                if (profiles != null)
                {
                    profile = profiles.FirstOrDefault(
                        p => p != null && p.id == id && p.Active);
                }
            }

            if (profile == null)
            {
                Plugin.Log.LogError($"--dwmp-continue: no saved profile {id}");
                if (sandboxDir != null)
                {
                    SaveRedirect.End();
                }

                yield break;
            }

            Core.currentProfile = profile;
            if (sandboxDir != null && !RedirectWorks(sandboxDir))
            {
                Core.currentProfile = null;
                SaveRedirect.End();
                yield break;
            }

            Plugin.Log.LogInfo(
                $"--dwmp-continue: loading your profile {id} (day "
                    + $"{profile.day})");
            Core.profiles = state.profiles;
            Singleton<UI>.Instance.StartCoroutine(
                Singleton<UI>.Instance.initLoadGame());
        }
    }
}
