namespace DarkwoodMP.World.Story
{
    /// What happens to a story step that a scripted event reaches.
    public enum StepRun : byte
    {
        /// It runs here.
        Run = 0,

        /// It runs here, and the host first tells the joiners a chapter
        /// starts (ChapterStart).
        StartChapter,

        /// It doesn't run here.
        Skip,

        /// It doesn't run here: the joiner asks the host (StoryRequest).
        AskHost,
    }
}
