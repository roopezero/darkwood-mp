namespace DarkwoodMP.World.Hideout
{
    /// What decides whether players are playing together in a world.
    public readonly struct TogetherFacts
    {
        /// ModHost.Session exists.
        public bool HasSession { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// WorldShare.Loading.
        public bool Loading { get; init; }

        /// This game is the host (Session.IsHost).
        public bool IsHost { get; init; }

        /// Session.Peers.Count.
        public int Peers { get; init; }

        /// This game shows the host's enemies (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }
    }
}
