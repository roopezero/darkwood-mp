namespace DarkwoodMP.World.Traders
{
    /// How TraderRules.Find found the trader.
    public enum TraderFound
    {
        /// He isn't here.
        None,

        /// The one his ref names (FindFacts.ByRef).
        ByRef,

        /// The nearest of his name (TraderMatch.Index).
        Nearest,
    }
}
