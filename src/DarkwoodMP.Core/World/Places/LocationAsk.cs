namespace DarkwoodMP.World.Places
{
    /// A client's LocationAsk: the location it is about to load, and
    /// whether it is a dream.
    public readonly struct LocationAsk
    {
        public bool Dream { get; init; }

        public string Name { get; init; }
    }
}
