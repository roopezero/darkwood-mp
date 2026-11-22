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
    /// Whether the game is in a world, loading one or at the main menu.
    /// </summary>
    public sealed partial class WorldShare
    {
        /// <summary>
        /// True while the game has a world loaded and running (not the main
        /// menu or a loading screen). The pause menu in a world counts as in
        /// it: Core.mainMenu is set for it too, but the world is still there.
        /// </summary>
        public static bool InWorld => GameStateRules.InWorld(InWorldNow());

        private static InWorldFacts InWorldNow()
        {
            bool menu = Core.mainMenu;
            return new InWorldFacts
            {
                Menu = menu,
                PauseMenu = menu && PauseMenuInWorld,
                Profile = Core.currentProfile != null,
                Player = Player.Instance != null,
            };
        }

        /// <summary>
        /// The menu is open over a world (the pause menu), not the main menu's
        /// own scene.
        /// </summary>
        public static bool PauseMenuInWorld
        {
            get
            {
                bool menu = Core.mainMenu;
                bool generator =
                    menu && Singleton<WorldGenerator>.Instance != null;
                return GameStateRules.PauseMenuInWorld(
                    menu, generator, generator && Core.worldGenFinished());
            }
        }

        /// <summary>
        /// True while the game is loading a world, by the game's own test for
        /// it (EventTriggers doesn't fire while this holds): the save loader
        /// (SaveManager.Load) makes every saved object again with
        /// Core.AddPrefab while Core.loadingGame is set, and keeps
        /// dontFireTriggers set until 2 s after it ends. Nothing made or
        /// changed then is a player's doing, so none of it is shared
        /// (Coop.Guards.Sharing).
        /// </summary>
        public static bool Loading =>
            Core.loadingGame || !Core.worldGenFinished()
                || (Singleton<SaveManager>.Instance != null
                    && Singleton<SaveManager>.Instance.dontFireTriggers);

        /// <summary>
        /// In a world that has finished loading: the player exists from early
        /// in the load, but the save (where they are, what they carry) is only
        /// applied as it ends. Stays true once it has been for a couple of
        /// seconds.
        /// </summary>
        public static bool WorldReady
        {
            get
            {
                var world = InWorldNow();
                bool inWorld = GameStateRules.InWorld(world);
                var looks = GameStateRules.LooksReady(
                    new InWorldFacts
                    {
                        Menu = world.Menu,
                        PauseMenu = world.PauseMenu,
                        Profile = world.Profile,
                        Player = world.Player,
                        Made = inWorld && Core.worldGenFinished(),
                        Loading = Core.loadingGame,
                        Forbid = Core.forbidInputs,
                    });

                // the game's WorldShare keeps when it started looking ready
                WorldShare share = Current;
                float since = -1f;
                if (share != null)
                {
                    since = share._readySince;
                }

                var step = GameStateRules.Ready(
                    looks,
                    new Seconds(since),
                    new Seconds(Time.realtimeSinceStartup));
                if (share != null)
                {
                    share._readySince = step.Since.Value;
                }

                return step.Ready;
            }
        }

        private float _readySince = -1f;

        /// <summary>
        /// The game's WorldShare (ModHost.Share), for the static questions
        /// above and below; null before Plugin.Load has made it.
        /// </summary>
        private static WorldShare Current
        {
            get
            {
                Plugin plugin = Plugin.Instance;
                if (plugin == null || plugin.Host == null)
                {
                    return null;
                }

                return plugin.Host.Share;
            }
        }

        /// <summary>
        /// The loaded world's scene handle: it changes whenever the world is
        /// loaded again.
        /// </summary>
        private static int ActiveScene =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

        /// <summary>
        /// A profile's save folder (prof&lt;id&gt;) under a folder laid out as
        /// 1_4Save.
        /// </summary>
        private static string ProfDir(string baseDir, int id) =>
            Path.Combine(baseDir, "prof" + id);
    }
}
