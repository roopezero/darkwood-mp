namespace DarkwoodMP.World.KeptCharacters
{
    /// What the host looks at to choose a joiner's character
    /// (Characters.OnRequest).
    public readonly struct KindFacts
    {
        /// The host keeps a record for the joiner's key.
        public bool Kept { get; init; }

        /// The record's Dead and Chapter (0: not known).
        public bool Dead { get; init; }

        public int Chapter { get; init; }

        /// Chapters.HostChapter: WorldGenerator.chapterID, 0 without one.
        public int HostChapter { get; init; }

        /// The NewCharacters setting ("copy" or "fresh").
        public string NewCharacters { get; init; }
    }
}
