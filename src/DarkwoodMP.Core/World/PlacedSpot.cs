namespace DarkwoodMP.World
{
    /// What a Placed key holds: the name's hash (NameKey) and the spot.
    public readonly struct PlacedSpot
    {
        public int Name { get; }

        public Vec2 Spot { get; }

        public PlacedSpot(int name, Vec2 spot)
        {
            Name = name;
            Spot = spot;
        }
    }
}
