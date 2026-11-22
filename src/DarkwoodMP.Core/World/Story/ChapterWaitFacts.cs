namespace DarkwoodMP.World.Story
{
    /// A joiner waits under a black screen for the host's chapter
    /// (Chapters.ChapterWait), one frame of it.
    public readonly struct ChapterWaitFacts
    {
        /// Time.realtimeSinceStartup.
        public Seconds Now { get; init; }

        /// The deadline (Chapters._chapterWaitUntil).
        public Seconds Until { get; init; }

        /// The active scene's handle isn't the one the wait began in.
        public bool SceneChanged { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }
    }
}
