namespace DarkwoodMP.World.Story
{
    /// Who set off a story step, and which (the parts of Chapters.Held
    /// that say whether a new request repeats the held one). The zero
    /// value is "none held".
    public readonly struct HeldStory
    {
        /// The joiner who asked.
        public ulong From { get; init; }

        public StoryKind Kind { get; init; }

        /// The chapter (0 unless Kind is Chapter).
        public int Chapter { get; init; }
    }
}
