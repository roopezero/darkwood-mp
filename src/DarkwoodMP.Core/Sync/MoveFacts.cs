namespace DarkwoodMP.Sync
{
    /// What the host looks at when a client's player says where it is.
    public readonly struct MoveFacts
    {
        /// The host has a track for this player.
        public bool Known { get; init; }

        /// What the host remembers about them, when Known.
        public PlayerMovement Was { get; init; }

        /// Where the client says its player is (PlayerState). Only x and z
        /// are checked; y comes back with the accepted position.
        public Vec3 Reported { get; init; }

        /// Time.realtimeSinceStartup.
        public Seconds Now { get; init; }

        /// A trip the host agreed to lands within TripLanding of Reported.
        /// Looked at only past TripLanding (MoveRules.AsksForTrip).
        public bool TripLanded { get; init; }
    }
}
