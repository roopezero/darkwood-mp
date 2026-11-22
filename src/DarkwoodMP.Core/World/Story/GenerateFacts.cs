namespace DarkwoodMP.World.Story
{
    /// Controller.generateChapter is about to run (Chapters.BeforeGenerate).
    public readonly struct GenerateFacts
    {
        /// A session is on (ModHost.Session is set and Active).
        public bool InSession { get; init; }

        /// generateSave: a new chapter, not a chapter's save loading.
        public bool NewChapter { get; init; }

        /// This game is a joiner (Session.IsClient).
        public bool IsClient { get; init; }
    }
}
