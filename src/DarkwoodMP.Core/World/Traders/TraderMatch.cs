namespace DarkwoodMP.World.Traders
{
    /// Which trader TraderRules.Find picked.
    public readonly struct TraderMatch
    {
        public TraderFound How { get; init; }

        /// With TraderFound.Nearest: his place in FindFacts.Candidates.
        public int Index { get; init; }
    }
}
