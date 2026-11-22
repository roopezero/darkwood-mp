namespace DarkwoodMP.World.Story
{
    /// Whether the story is shared now (StoryRules.Sharing).
    public readonly struct SharingFacts
    {
        /// The world is loaded and playing (WorldShare.WorldReady).
        public bool WorldReady { get; init; }

        /// This game hosts (Session.IsHost).
        public bool Host { get; init; }

        /// How many others are in the session (Session.Peers.Count).
        public int Peers { get; init; }

        /// This game plays the host's world (NetEnemies.ClientMode).
        public bool Client { get; init; }
    }
}
