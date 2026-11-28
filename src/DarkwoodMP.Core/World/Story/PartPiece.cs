namespace DarkwoodMP.World.Story
{
    /// One piece of an epilogue part (a direct child of its location): its
    /// name and where it stands in the location (x and z).
    public readonly struct PartPiece
    {
        public string Name { get; init; }

        public Vec2 At { get; init; }
    }
}
