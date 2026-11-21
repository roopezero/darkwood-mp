namespace DarkwoodMP.World.Story
{
    /// HeldRules.Next's answer: what to do, and the new settle state.
    public readonly struct HeldStep
    {
        public HeldAction Action { get; init; }

        /// The new HeldFacts.Ready.
        public bool Ready { get; init; }

        /// The new HeldFacts.ReadySince.
        public Seconds ReadySince { get; init; }
    }
}
