using System.Collections.Generic;

namespace DarkwoodMP.Combat.Damage
{
    /// A hit on a remote player's stand-in, as the host sends it to that
    /// player (PlayerDamageMessage).
    public readonly struct StandInHit
    {
        public float Damage { get; init; }

        /// The hit is a shadow's (Player.getHitByShadow).
        public bool Shadow { get; init; }

        /// The attacker's name, "?" when there is none.
        public string Attacker { get; init; }

        /// Its effects; null for none.
        public IReadOnlyList<SentEffect> Effects { get; init; }
    }
}
