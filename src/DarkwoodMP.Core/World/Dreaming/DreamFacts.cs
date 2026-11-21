namespace DarkwoodMP.World.Dreaming
{
    /// The game's dream state in our game (Dreams and Core).
    public readonly struct DreamFacts
    {
        /// Singleton<Dreams>.Instance is there. Without it, every other
        /// field is false.
        public bool HasDreams { get; init; }

        /// Dreams.dreaming.
        public bool Dreaming { get; init; }

        /// Dreams.dreamPrepared.
        public bool Prepared { get; init; }

        /// Dreams.switchingDream.
        public bool Switching { get; init; }

        /// Dreams.wantToDream.
        public bool WantToDream { get; init; }

        /// Core.EnteringDream (not a Dreams field: the shell reads it
        /// even without Dreams, but no rule looks at it then).
        public bool EnteringDream { get; init; }

        /// Core.loadingGame.
        public bool LoadingGame { get; init; }
    }
}
