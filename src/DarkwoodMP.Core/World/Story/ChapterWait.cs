namespace DarkwoodMP.World.Story
{
    /// ChapterRules.Extend's answer: the new deadline, and whether a wait
    /// starts.
    public readonly struct ChapterWait
    {
        /// The new Chapters._chapterWaitUntil.
        public Seconds Until { get; init; }

        /// No wait was on: start one (Chapters.ChapterWait).
        public bool Starts { get; init; }
    }
}
