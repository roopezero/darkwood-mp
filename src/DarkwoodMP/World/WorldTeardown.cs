using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A world being left: back to the main menu, a chapter or save loaded, or the game quitting. Set by the
    /// game's own calls that load the next scene, cleared once it has loaded.
    ///
    /// Why: leaving a chapter 2 world threw a NullReferenceException for each banshee, in every game, the host's
    /// own Core.returnToMainMenu too (51 in a joiner's game with the swamp loaded). It's the game's own teardown:
    /// Unity destroys the scene's objects in no fixed order, and Character.OnDestroy reaches into other objects of
    /// the same scene. Banshee prefabs list their own child "banshee_ambient_01" in attachedGameObjects, and
    /// removeAttachedGameObjects looks it up in the FX and UI SpawnPools, which are objects of the scene too: when
    /// a pool has gone first, PoolManager.Pools["FX"] or ["UI"] is null. Past that, onBansheeOutOfSightOfPlayer
    /// makes a new banshee overlay under the UI (UI.initBansheeOverlay), which throws when the UI has gone first.
    /// Each throw ended that banshee's OnDestroy there.
    /// </summary>
    static class WorldTeardown
    {
        public static bool Active { get; private set; }

        public static void Init()
        {
            Application.quitting += () => Active = true;
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (mode == LoadSceneMode.Single)
                {
                    Active = false;
                }
            };
        }

        internal static void Begin() => Active = true;
    }

    [HarmonyPatch(typeof(Core), nameof(Core.returnToMainMenu))]
    static class TeardownMenuPatch
    {
        static void Prefix() => WorldTeardown.Begin();
    }

    [HarmonyPatch(typeof(Core), nameof(Core.restartGame))]
    static class TeardownRestartPatch
    {
        static void Prefix() => WorldTeardown.Begin();
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.generateChapter))]
    static class TeardownChapterPatch
    {
        static void Prefix() => WorldTeardown.Begin();
    }

    /// <summary>
    /// A character destroyed with its world does only what outlives the world (a static flag, sounds the
    /// AudioController plays, which isn't destroyed with the scene). The rest of Character.OnDestroy takes it off
    /// the player's attackers, its location, the night's characters, and the banshee's sight and overlay: all of
    /// them objects of the same scene, going with it.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.OnDestroy))]
    static class CharacterTeardownPatch
    {
        static bool Prefix(Character __instance)
        {
            if (!WorldTeardown.Active)
            {
                return true;
            }
            if (__instance.forestSpirit)
            {
                Core.chasedByForestSpirit = false;
                AudioController.Stop("my_heaven", 10f);
            }
            var p = Player.Instance;
            if (__instance.banshee && (object)p != null && p.bansheeAgitatedSoundAO != null)
            {
                p.bansheeAgitatedSoundAO.Stop(1f);
                p.bansheeAgitatedSoundAO = null;
            }
            return false;
        }
    }
}
