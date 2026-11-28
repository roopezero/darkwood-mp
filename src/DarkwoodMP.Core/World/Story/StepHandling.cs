namespace DarkwoodMP.World.Story
{
    /// What an EpilogueStep message does here (EpilogueSync.OnStep).
    public enum StepHandling : byte
    {
        /// Nothing.
        Ignore = 0,

        /// Someone set the Being on fire (Burned).
        Burned,

        /// The host: a joiner crawled to its death (Crawled).
        Crawled,

        /// A joiner: keep the host's outcomes (HeardBurned).
        Outcomes,

        /// A joiner: the host released the pan (RunPan).
        Pan,

        /// The host: a joiner asks for its part's layout (SendLayout).
        LayoutAsk,

        /// A joiner: the host's layout of a part (ApplyLayout).
        Layout,

        /// A new joiner: the host's epilogue is on, in this part
        /// (JoinPart).
        JoinPart,
    }
}
