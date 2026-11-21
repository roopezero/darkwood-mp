namespace DarkwoodMP.World.Nights
{
    /// Whether the host records and sends night objects.
    public readonly struct NightHostFacts
    {
        /// The session NightObjects registered with.
        public bool HasSession { get; init; }

        /// It is the host (Session.IsHost).
        public bool IsHost { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }
    }
}
