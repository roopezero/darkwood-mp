namespace DarkwoodMP.World.Places
{
    /// Where a location stands and how it is turned (degrees about y).
    public readonly struct Spot
    {
        public Vec3 Pos { get; init; }

        public int Turn { get; init; }
    }
}
