namespace DarkwoodMP.World.Dreaming
{
    /// What StoryDreamRules needs to know of a story dream about to start
    /// in our game, or asked for by a joiner.
    public readonly struct StoryDreamFacts
    {
        /// ModHost.Session is there.
        public bool HasSession { get; init; }

        /// We host the session.
        public bool IsHost { get; init; }

        /// NetEnemies.ClientMode: we play in the host's world.
        public bool ClientMode { get; init; }

        /// The dream's preset is one every player dreams together
        /// (StoryDreamRules.Together).
        public bool Together { get; init; }

        /// A shared dream is on already (DreamSync.Shared).
        public bool Shared { get; init; }

        /// The shared dream on now is this one.
        public bool SameDream { get; init; }

        /// Our player is in a dream, going into one or out of one
        /// (DreamRules.InDream).
        public bool InDream { get; init; }

        /// Core.loadingGame.
        public bool LoadingGame { get; init; }

        /// EpilogueSync.Playing.
        public bool EpiloguePlaying { get; init; }
    }
}
