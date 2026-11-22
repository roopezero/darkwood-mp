namespace DarkwoodMP.World.Story
{
    /// One frame of a joiner's wait for the host's chapter.
    public enum ChapterWaitStep : byte
    {
        /// Wait on.
        Waits = 0,

        /// The world changed or was left: the wait ends quietly, and the
        /// deadline goes to 0.
        Over,

        /// The chapter didn't come: say so, let inputs go and fade in.
        TimedOut,
    }
}
