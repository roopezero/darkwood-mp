using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The game doesn't stop while others are playing in it. Darkwood pauses (Core.pause: time scale 0, sound
    /// paused) for the pause menu, the map, the journal, dialogue, the padlock, the level-up and skill menus.
    /// The host's game runs the enemies and the clock for everyone, and a client's shows them, so in co-op the
    /// pause only marks the game paused (Core.Paused, which stops the player's own input as before) and the
    /// world goes on, as in other co-op games. While a menu is open your character stands still, and enemies
    /// can still get you.
    ///
    /// The same goes for slow motion: the active-skill wheel (ActiveSkillsMenu, time scale 0.2 while it's open) and
    /// a scripted event's timeScale step (Controller.alterTimeScale). On the host they would slow the enemies, fire
    /// and clock of everyone's world. In co-op the time scale stays 1 (the wheel still stops your own input).
    /// </summary>
    public static class CoopPause
    {
        /// <summary>Whether other players share this world right now.</summary>
        public static bool InCoop
        {
            get
            {
                var s = Net.Session.Instance;
                return s != null && s.Active && s.Connected && WorldShare.InWorld && (s.IsClient || s.Peers.Count > 0);
            }
        }

        /// <summary>
        /// Call every frame. A game that was paused before co-op started (the host in a menu as someone joins)
        /// starts running again; and a character standing still in a menu doesn't keep sliding.
        /// </summary>
        public static void Update()
        {
            if (!InCoop || !Core.Paused) return;
            if (Time.timeScale == 0f)
            {
                Plugin.Log.LogInfo("Co-op: the game was paused; it runs on while others play");
                Core.setTimeScale(1f);
                AudioController.UnpauseAll(0.5f);
            }
            var p = Player.Instance;
            if (p != null && p.alive)
            {
                var rb = p.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic) rb.velocity = Vector3.zero;
            }
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.pause))]
    static class CoopPausePatch
    {
        static bool Prefix()
        {
            if (!CoopPause.InCoop) return true;
            Core.Paused = true; // menus and input behave as paused; time and sound go on
            return false;
        }
    }

    /// <summary>Slow motion (the active-skill wheel) keeps time at its normal rate in co-op.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.setTimeScale), typeof(float))]
    static class CoopTimeScalePatch
    {
        static void Prefix(ref float _timeScale)
        {
            if (_timeScale > 0f && _timeScale != 1f && CoopPause.InCoop) _timeScale = 1f;
        }
    }

    /// <summary>A scripted event's slow motion (GameEvent timeScale) doesn't run in co-op.</summary>
    [HarmonyPatch(typeof(Controller), nameof(Controller.alterTimeScale), typeof(float), typeof(float))]
    static class CoopAlterTimeScalePatch
    {
        static bool Prefix(ref System.Collections.IEnumerator __result)
        {
            if (!CoopPause.InCoop) return true;
            __result = Nothing();
            return false;
        }

        private static System.Collections.IEnumerator Nothing() { yield break; }
    }
}
