using DarkwoodMP.World.Pause;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The game doesn't stop while others are playing in it. Darkwood pauses
    /// (Core.pause: time scale 0, sound paused) for the pause menu, the map,
    /// the journal, dialogue, the padlock, the level-up and skill menus.
    /// The host's game runs the enemies and the clock for everyone, and a
    /// client's shows them, so in co-op the pause only marks the game paused
    /// (Core.Paused, which stops the player's own input as it does alone) and
    /// the world goes on, as in other co-op games. While a menu is open your
    /// character stands still, and enemies can still get you.
    ///
    /// The same goes for slow motion: the active-skill wheel (ActiveSkillsMenu,
    /// time scale 0.2 while it's open) and a scripted event's timeScale step
    /// (Controller.alterTimeScale). On the host they would slow the enemies,
    /// fire and clock of everyone's world. In co-op the time scale stays 1 (the
    /// wheel still stops your own input).
    /// </summary>
    public static class CoopPause
    {
        /// <summary>Whether other players share this world right now.</summary>
        public static bool InCoop
        {
            get
            {
                return PauseRules.InCoop(CoopOf(Net.Session.Instance));
            }
        }

        /// <summary>
        /// Call every frame. A game that was paused before co-op started (the
        /// host in a menu as someone joins) starts running again; and a
        /// character standing still in a menu doesn't keep sliding.
        /// </summary>
        public static void Update()
        {
            var facts = new PauseFacts
            {
                InCoop = InCoop,
                Paused = Core.Paused,
            };

            // the player is looked at only when the rules will
            Player p = Player.Instance;
            Rigidbody rb = null;
            if (facts.InCoop && facts.Paused && p != null && p.alive)
            {
                rb = p.GetComponent<Rigidbody>();
            }

            PauseTick tick = PauseRules.Tick(FactsOf(facts, p, rb));
            if (tick.Unfreeze)
            {
                Plugin.Log.LogInfo(
                    "Co-op: the game was paused; it runs on while others play");
                Core.setTimeScale(PauseRules.Normal);
                AudioController.UnpauseAll(PauseRules.SoundFade);
            }

            if (tick.StandStill)
            {
                rb.velocity = Vector3.zero;
            }
        }

        private static CoopFacts CoopOf(Net.Session s)
        {
            if (s == null)
            {
                return default;
            }

            return new CoopFacts
            {
                Active = s.Active,
                Connected = s.Connected,
                InWorld = WorldShare.InWorld,
                IsClient = s.IsClient,
                Peers = s.Peers.Count,
            };
        }

        private static PauseFacts FactsOf(
            PauseFacts facts, Player p, Rigidbody rb)
        {
            return facts with
            {
                TimeScale = Time.timeScale,
                Alive = p != null && p.alive,
                HasBody = rb != null,
                Kinematic = rb != null && rb.isKinematic,
            };
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.pause))]
    static class CoopPausePatch
    {
        static bool Prefix()
        {
            if (PauseRules.RunsGamePause(CoopPause.InCoop))
            {
                return true;
            }

            // menus and input behave as paused; time and sound go on
            Core.Paused = true;
            return false;
        }
    }

    /// <summary>
    /// Slow motion (the active-skill wheel) keeps time at its normal rate in
    /// co-op.
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.setTimeScale), typeof(float))]
    static class CoopTimeScalePatch
    {
        static void Prefix(ref float _timeScale)
        {
            _timeScale = PauseRules.TimeScaleFor(_timeScale, CoopPause.InCoop);
        }
    }

    /// <summary>
    /// A scripted event's slow motion (GameEvent timeScale) doesn't run in
    /// co-op.
    /// </summary>
    [HarmonyPatch(
        typeof(Controller), nameof(Controller.alterTimeScale), typeof(float),
        typeof(float))]
    static class CoopAlterTimeScalePatch
    {
        static bool Prefix(ref System.Collections.IEnumerator __result)
        {
            if (PauseRules.RunsScriptedSlowMotion(CoopPause.InCoop))
            {
                return true;
            }

            __result = Nothing();
            return false;
        }

        private static System.Collections.IEnumerator Nothing()
        {
            yield break;
        }
    }
}
