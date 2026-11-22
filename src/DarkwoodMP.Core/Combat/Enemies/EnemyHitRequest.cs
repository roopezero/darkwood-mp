using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Combat.Enemies
{
    /// Our player's hit on one of the host's enemies, as EnemyHit carries
    /// it.
    public readonly struct EnemyHitRequest
    {
        public uint Id { get; init; }

        public float Damage { get; init; }

        /// EnemyHitMessage's Cut, ByPlayer, Interrupt and Normal.
        public byte Flags { get; init; }

        /// The weapon's effects; none for null.
        public IReadOnlyList<SentEffect> Effects { get; init; }

        /// The weapon as its save state's JSON; "" for none.
        public string Weapon { get; init; }
    }
}
