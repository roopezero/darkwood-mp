using System;

namespace DarkwoodMP.World.Doors
{
    /// A door before or after one of its game calls (the plugin's
    /// WorldActions.DoorState): if any of it differs, the call changed
    /// the door and is sent.
    public readonly struct DoorState : IEquatable<DoorState>
    {
        /// Door.opened.
        public bool Opened { get; init; }

        /// Door.barricaded.
        public bool Barricaded { get; init; }

        /// Door.destroyed.
        public bool Destroyed { get; init; }

        /// Door.health.
        public int Health { get; init; }

        /// Door.barricadeHealth.
        public int BarricadeHealth { get; init; }

        public bool Equals(DoorState other) =>
            Opened == other.Opened
            && Barricaded == other.Barricaded
            && Destroyed == other.Destroyed
            && Health == other.Health
            && BarricadeHealth == other.BarricadeHealth;

        public override bool Equals(object obj) =>
            obj is DoorState other && Equals(other);

        public override int GetHashCode() =>
            Health * 397 ^ BarricadeHealth;
    }
}
