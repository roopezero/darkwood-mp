namespace DarkwoodMP.World.Spectating
{
    /// SpectatorRules.Watch's answer: what to do, and the new state.
    public readonly struct WatchStep
    {
        public WatchAction Action { get; init; }

        /// The new WatchFacts.Nobody.
        public bool Nobody { get; init; }

        /// The new WatchFacts.NobodySince.
        public Seconds NobodySince { get; init; }
    }
}
