namespace DarkwoodMP.World
{
    /// One level of an object's hierarchy: its name and sibling index.
    public readonly struct PlaceLevel
    {
        public string Name { get; init; }

        public int Index { get; init; }
    }
}
