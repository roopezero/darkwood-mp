using UnityEngine;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A world being left: back to the main menu, a chapter or save loaded, or
    /// the game quitting. Set by the game's own calls that load the next scene,
    /// cleared once it has loaded.
    ///
    /// Why: leaving a chapter 2 world threw a NullReferenceException for each
    /// banshee, in every game, the host's own Core.returnToMainMenu too (51 in
    /// a joiner's game with the swamp loaded). It's the game's own teardown:
    /// Unity destroys the scene's objects in no fixed order, and
    /// Character.OnDestroy reaches into other objects of the same scene.
    /// Banshee prefabs list their own child "banshee_ambient_01" in
    /// attachedGameObjects, and removeAttachedGameObjects looks it up in the FX
    /// and UI SpawnPools, which are objects of the scene too: when a pool has
    /// gone first, PoolManager.Pools["FX"] or ["UI"] is null. Past that,
    /// onBansheeOutOfSightOfPlayer makes a new banshee overlay under the UI
    /// (UI.initBansheeOverlay), which throws when the UI has gone first. Each
    /// throw ended that banshee's OnDestroy there.
    /// </summary>
    internal sealed class WorldTeardown
    {
        /// It lives as long as the game (ModHost.Teardown): leaving a world
        /// happens alone too.
        public WorldTeardown()
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

        public bool Active { get; private set; }

        internal void Begin() => Active = true;

        /// <summary>
        /// Whether the game's Character.OnDestroy runs. A character destroyed
        /// with its world does only what outlives the world (a static flag,
        /// sounds the AudioController plays, which isn't destroyed with the
        /// scene). The rest of Character.OnDestroy takes it off the player's
        /// attackers, its location, the night's characters, and the
        /// banshee's sight and overlay: all of them objects of the same
        /// scene, going with it.
        /// </summary>
        public bool RunsOnDestroy(Character character)
        {
            if (!Active)
            {
                return true;
            }

            if (character.forestSpirit)
            {
                Core.chasedByForestSpirit = false;
                AudioController.Stop("my_heaven", 10f);
            }

            var p = Player.Instance;
            if (character.banshee && (object)p != null
                && p.bansheeAgitatedSoundAO != null)
            {
                p.bansheeAgitatedSoundAO.Stop(1f);
                p.bansheeAgitatedSoundAO = null;
            }

            return false;
        }
    }
}
