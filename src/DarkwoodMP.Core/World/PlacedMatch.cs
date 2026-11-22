namespace DarkwoodMP.World
{
    /// Which candidate is nearest the spot, and whether it stands on it.
    /// The zero value is "none within PlacedSlack".
    public readonly struct PlacedMatch
    {
        public bool Found { get; }

        public int Index { get; }

        public bool OnSpot { get; }

        public PlacedMatch(int index, bool onSpot)
        {
            Found = true;
            Index = index;
            OnSpot = onSpot;
        }
    }
}
