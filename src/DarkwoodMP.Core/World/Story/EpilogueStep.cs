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

        /// A joiner asks for the layout of the part it is in.
        LayoutAsk = 5,

        /// The host's layout of a part: where its pieces stand.
        Layout = 6,
    }
}
