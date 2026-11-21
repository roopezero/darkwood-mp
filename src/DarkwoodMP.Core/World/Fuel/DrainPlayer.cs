namespace DarkwoodMP.World.Fuel
{
    /// One stand-in for another player (NetPlayerProxy.All), for the
    /// nearest player to a generator.
    public readonly struct DrainPlayer
    {
        /// The stand-in is there (not null).
        public bool Exists { get; init; }

        /// The player is where the host's game doesn't run
        /// (NetPlayerProxy.Away).
        public bool Away { get; init; }

        /// The player is in a location (InLocation isn't null).
        public bool InLocation { get; init; }

        /// The test dummy (Owner is PlayerSync.DummyId).
        public bool Dummy { get; init; }

        /// Where the stand-in is.
        public Vec2 Pos { get; init; }
    }
}
