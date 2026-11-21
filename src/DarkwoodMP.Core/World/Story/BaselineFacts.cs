namespace DarkwoodMP.World.Story
{
    /// Whether the story's baseline is kept while it isn't shared
    /// (StoryRules.KeepsBaseline).
    public readonly struct BaselineFacts
    {
        /// There is a baseline (FlagsSync._baseline isn't null).
        public bool Known { get; init; }

        /// There is a session and it is active (Session.Instance isn't null
        /// and Session.Active).
        public bool Active { get; init; }

        /// The player is in a world (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// The active scene is the one the baseline is of (its handle is
        /// FlagsSync._baselineScene).
        public bool SameScene { get; init; }

        /// A world is being loaded (WorldShare.Loading).
        public bool Loading { get; init; }

        /// This game hosts (Session.IsHost).
        public bool Host { get; init; }

        /// How many others are in the session (Session.Peers.Count).
        public int Peers { get; init; }

        /// This game plays the host's world (NetEnemies.ClientMode).
        public bool Client { get; init; }
    }
}
