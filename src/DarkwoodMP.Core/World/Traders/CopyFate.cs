namespace DarkwoodMP.World.Traders
{
    /// What becomes of one of the host's copies of a joiner's morning
    /// trader (TraderCopyRules.Fate).
    public enum CopyFate
    {
        /// Still the joiner's: kept, and looked at again.
        Keep,

        /// No longer a copy: left as he is, and forgotten.
        Forget,

        /// Taken away (Location.despawnTrader, or destroyed when he isn't
        /// the hideout's trader any more), and forgotten.
        TakeAway,
    }
}
