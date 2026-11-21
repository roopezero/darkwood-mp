namespace DarkwoodMP.World.Story
{
    /// The game's Dreams (Singleton<Dreams>.Instance) and its own flags.
    /// The zero value is "no Dreams".
    public readonly struct DreamFacts
    {
        /// There is a Dreams.
        public bool Exists { get; init; }

        /// Core.EnteringDream (a static: true even with no Dreams).
        public bool Entering { get; init; }

        /// Dreams.dreaming.
        public bool Dreaming { get; init; }

        /// Dreams.dreamPrepared.
        public bool Prepared { get; init; }

        /// Dreams.switchingDream.
        public bool Switching { get; init; }

        /// Dreams.wantToDream.
        public bool Wanted { get; init; }

        /// Dreams.currentTransition is set and isPlaying.
        public bool Transition { get; init; }

        /// Dreams.preset is set.
        public bool HasPreset { get; init; }

        /// Dreams.preset.name as it is, when HasPreset.
        public string Preset { get; init; }

        /// Core.getTrueLocationName(Dreams.preset.name), when HasPreset.
        public string TrueName { get; init; }
    }
}
