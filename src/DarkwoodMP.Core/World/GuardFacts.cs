namespace DarkwoodMP.World
{
    /// The session and the world, for whether this game shares its world
    /// right now (GuardRules.Sharing).
    public readonly struct GuardFacts
    {
        /// A call from another game is being made (Guards.Replaying).
        public bool Replaying { get; init; }

        /// A world is loaded (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// The world is still loading (WorldShare.Loading).
        public bool Loading { get; init; }

        /// This game hosts (Session.IsHost).
        public bool Host { get; init; }

        /// How many others are in the session (Session.Peers.Count).
        public int Peers { get; init; }

        /// This game plays the host's world (NetEnemies.ClientMode).
        public bool Client { get; init; }
    }
}
