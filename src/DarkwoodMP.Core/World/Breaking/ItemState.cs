using System;

namespace DarkwoodMP.World.Breaking
{
    /// What a hit can change about an item: its health, whether it is
    /// destroyed, and its shadow armour's health (-1 with none).
    public readonly struct ItemState : IEquatable<ItemState>
    {
        public int Health { get; init; }

        public bool Destroyed { get; init; }

        /// ShadowArmor.destHealth, or -1 when it has no shadow armour.
        public float Shadow { get; init; }

        /// The same in every part; the shadow's float is compared exactly.
        public bool Equals(ItemState other)
        {
            return Health == other.Health
                && Destroyed == other.Destroyed
                && Shadow == other.Shadow;
        }

        public override bool Equals(object obj) =>
            obj is ItemState other && Equals(other);

        public override int GetHashCode() => Health;
    }
}
