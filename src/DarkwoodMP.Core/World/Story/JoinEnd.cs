namespace DarkwoodMP.World.Story
{
    /// How a joiner's way into the host's epilogue ends
    /// (EpilogueSync.JoinEpilogue), once it has stopped waiting.
    public enum JoinEnd : byte
    {
        /// Our player is in the epilogue.
        Joined = 0,

        /// The epilogue isn't Playing any more: nothing to do.
        Stopped,

        /// It didn't start: Playing goes false, and the end screen alone.
        Fallback,
    }
}
