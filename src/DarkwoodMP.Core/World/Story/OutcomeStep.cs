namespace DarkwoodMP.World.Story
{
    /// What EpilogueOutcomes.Start does (EpilogueSync.OutcomesStart).
    public enum OutcomeStep : byte
    {
        /// Nothing: no outcomes, not Playing, or no session.
        None = 0,

        /// The host sends its burnedForest (SendOutcomes).
        Send,

        /// A joiner keeps its own: the host's is unheard, or the same.
        Keep,

        /// A joiner takes the host's, with a warning.
        TakeHost,
    }
}
