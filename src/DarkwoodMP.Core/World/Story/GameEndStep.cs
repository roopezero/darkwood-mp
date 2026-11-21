namespace DarkwoodMP.World.Story
{
    /// What a joiner does with the host's GameEnd.
    public enum GameEndStep : byte
    {
        /// Not from the host: nothing.
        Ignored = 0,

        /// Not the epilogue: leave and see the ending alone
        /// (Chapters.SoloEnding, with the message's kind).
        Solo,

        /// Phase 0, out of a world: Playing, Reset, save the character and
        /// the notice, and nothing more.
        BeginEpilogue,

        /// Phase 0 in a world: as BeginEpilogue, then JoinEpilogue.
        JoinEpilogue,

        /// The credits: Playing goes false and the joiner leaves.
        Leave,

        /// As Leave, then Chapters.ToCredits.
        LeaveToCredits,
    }
}
