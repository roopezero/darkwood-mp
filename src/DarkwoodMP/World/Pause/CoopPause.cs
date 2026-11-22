using System.Collections;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Pause
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
    ///
    /// What happens is the core's PauseRules; PausePatches hand the game's
    /// calls here.
    /// </summary>
    internal sealed class CoopPause
    {
        private readonly CoopSession _coop;

        public CoopPause(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>Whether other players share this world right now.</summary>
        public bool InCoop
        {
            get
            {
                return PauseRules.InCoop(CoopOf(_coop.Net));
            }
        }

        /// <summary>
        /// Call every frame. A game that was paused before co-op started (the
        /// host in a menu as someone joins) starts running again; and a
        /// character standing still in a menu doesn't keep sliding.
        /// </summary>
        public void Update()
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

        private static CoopFacts CoopOf(Session s)
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

        /// <summary>
        /// Core.pause: whether the game's pause runs. In co-op it only marks
        /// the game paused: menus and input behave as paused; time and sound
        /// go on.
        /// </summary>
        public bool Pause()
        {
            if (PauseRules.RunsGamePause(InCoop))
            {
                return true;
            }

            Core.Paused = true;
            return false;
        }

        /// <summary>
        /// Core.setTimeScale: the scale it gets. Slow motion (the
        /// active-skill wheel) keeps time at its normal rate in co-op.
        /// </summary>
        public float TimeScaleFor(float scale)
        {
            return PauseRules.TimeScaleFor(scale, InCoop);
        }

        /// <summary>
        /// Controller.alterTimeScale: whether a scripted event's slow motion
        /// runs. When it doesn't, the coroutine it returns does nothing.
        /// </summary>
        public bool AlterTimeScale(ref IEnumerator result)
        {
            if (PauseRules.RunsScriptedSlowMotion(InCoop))
            {
                return true;
            }

            result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }
}
