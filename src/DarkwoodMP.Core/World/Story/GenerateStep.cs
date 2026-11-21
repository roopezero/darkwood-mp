namespace DarkwoodMP.World.Story
{
    /// What happens to Controller.generateChapter.
    public enum GenerateStep : byte
    {
        /// It runs, and nothing else happens: no session, or a save loads.
        Run = 0,

        /// A joiner: it doesn't run (with a warning).
        Skip,

        /// The host: send ChapterStart, mark Generating, and run it.
        Start,
    }
}
