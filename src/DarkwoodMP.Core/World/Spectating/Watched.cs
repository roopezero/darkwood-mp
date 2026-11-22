namespace DarkwoodMP.World.Spectating
{
    /// A remote player, as watching sees it (Sync.Players.RemotePlayer).
    public readonly struct Watched
    {
        /// RemotePlayer.Owner.
        public ulong Owner { get; init; }

        /// The test's dummy player (Owner is PlayerSync.DummyId): never
        /// watched.
        public bool Dummy { get; init; }

        /// RemotePlayer.Alive.
        public bool Alive { get; init; }

        /// RemotePlayer.Spectating: dead for good, watching too.
        public bool Spectating { get; init; }
    }
}
