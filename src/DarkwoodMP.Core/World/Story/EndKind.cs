namespace DarkwoodMP.World.Story
{
    /// How the game ends (Chapters.EndKind, with the same values: GameEnd
    /// sends it as a byte).
    public enum EndKind : byte
    {
        /// No ending: the plugin's EndKind has no zero, and a rule returns
        /// this when nothing ends.
        None = 0,

        /// The epilogue, played together.
        Epilogue = 1,

        /// The older end screen: the session ends at once.
        EndScreen = 2,
    }
}
