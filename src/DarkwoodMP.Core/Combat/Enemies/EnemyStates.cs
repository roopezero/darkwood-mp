using System.Collections.Generic;

namespace DarkwoodMP.Combat.Enemies
{
    /// An EnemyState as read: the host's clock and its enemies.
    public sealed class EnemyStates
    {
        /// The host's Time.unscaledTime when it was sent.
        public float Sent { get; init; }

        public List<EnemyFrame> Frames { get; init; }
    }
}
