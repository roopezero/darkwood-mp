namespace DarkwoodMP.World.Story
{
    /// A story step that only the host's game runs (Chapters.StoryKind,
    /// with the same values: StoryRequest sends it as a byte).
    public enum StoryKind : byte
    {
        /// Not a story step.
        None = 0,

        /// A new chapter: transportPlayerToObject with activeModifier.
        Chapter = 1,

        /// The epilogue: startDream into "epilog_part1a_dream".
        Epilogue = 2,

        /// The older end screen: endGame.
        EndScreen = 3,
    }
}
