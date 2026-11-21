namespace DarkwoodMP.World.Story
{
    /// The epilogue's message steps (EpilogueSync.Step, with the same
    /// values: EpilogueStep sends it as a byte).
    public enum EpilogueStep : byte
    {
        /// No such step.
        None = 0,

        /// The host released the pan.
        Pan = 1,

        /// The host's outcomes: "1" burned, anything else not.
        Outcomes = 2,

        /// A joiner crawled to its death.
        Crawled = 3,

        /// Someone set the Being on fire.
        Burn = 4,
    }
}
