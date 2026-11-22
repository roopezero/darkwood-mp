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
    }
}
