using DarkwoodMP.Combat;
using DarkwoodMP.World.Throws;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// ThrowSync on a client: the explosions on our own player that the host
    /// reports.
    internal sealed partial class ThrowSync
    {
        /// <summary>
        /// Client: whether what's hitting our player now is an explosion, which
        /// the host reports through our proxy (EnemyAreaHits.Explosion). Not
        /// while we're away in a place of our own (a location, a dream):
        /// explosions there happen only here.
        /// </summary>
        internal static bool HostReports() =>
            ThrownHitRules.HostMayReport(SelfHit()) && InExplosion();

        /// <summary>
        /// What the client looks at for an explosion on its own player.
        /// </summary>
        private static SelfHitFacts SelfHit()
        {
            var facts = new SelfHitFacts
            {
                ClientMode = NetEnemies.ClientMode,
                Depth = ThrownHits.Depth,
                Flame = ThrownHits.CurrentFlame != null,
            };

            // Only a client inside something thrown asks: where the player
            // is costs singleton lookups on every hit.
            if (!facts.ClientMode || facts.Depth <= 0)
            {
                return facts;
            }

            return facts with { Away = EnemyAreaHits.PlayerAway() };
        }

        private static bool InExplosion()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 12; i++)
            {
                System.Diagnostics.StackFrame frame = st.GetFrame(i);
                System.Type declaringType = null;
                if (frame != null)
                {
                    System.Reflection.MethodBase method = frame.GetMethod();
                    if (method != null)
                    {
                        declaringType = method.DeclaringType;
                    }
                }

                if (declaringType == typeof(Explodes))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Client: whether an effect starting on a character is one the host
        /// reports (CharacterEffects.activate): our own player's, caught in
        /// an explosion, as for HostReports.
        /// </summary>
        internal static bool HostReportsEffect(CharacterEffects effects)
        {
            var player = Player.Instance;
            bool ours = effects != null && player != null
                && effects == player.effects;
            // Called straight from the patch, as HostReports is, so
            // InExplosion's look at the stack starts at the same frame.
            return ThrownHitRules.HostMayReportEffect(SelfHit(), ours)
                && InExplosion();
        }
    }
}
