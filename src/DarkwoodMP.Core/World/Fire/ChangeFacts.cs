namespace DarkwoodMP.World.Fire
{
    /// What the host knows when a fire starts (Burn.Start) or goes out
    /// (Burn.stop) in its world.
    public readonly struct ChangeFacts
    {
        /// The Burn is there (not null).
        public bool Exists { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }

        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        /// How many players are joined (Session.Peers.Count).
        public int Peers { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// What is burning (FireRules.KindOf).
        public FireKind Kind { get; init; }

        /// It has a valid ObjRef.
        public bool Named { get; init; }
    }
}
