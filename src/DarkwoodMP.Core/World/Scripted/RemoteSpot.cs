namespace DarkwoodMP.World.Scripted
{
    /// Client: another player's puppet (PlayerSync.Remotes), for whether
    /// they are by a location.
    public readonly struct RemoteSpot
    {
        /// The puppet has a root object.
        public bool Shown { get; init; }

        /// They are in a separate location or a dream (RemotePlayer.Away).
        public bool Away { get; init; }

        /// It is the map test's dummy (PlayerSync.DummyId).
        public bool Dummy { get; init; }

        /// On the ground plane.
        public Vec2 Pos { get; init; }
    }
}
