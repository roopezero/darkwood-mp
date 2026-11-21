namespace DarkwoodMP.World.Story
{
    /// What EpilogueOutcomes.goToCredits does to the session
    /// (EpilogueSync.BeforeCredits).
    public enum CreditsStep : byte
    {
        /// Nothing: no session, or the epilogue isn't Playing.
        None = 0,

        /// The host: Playing goes false, GameEnd phase 1, the server stops.
        HostStops,

        /// A joiner: Playing goes false, and it leaves.
        JoinerLeaves,
    }
}
