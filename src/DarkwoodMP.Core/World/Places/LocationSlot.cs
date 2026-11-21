namespace DarkwoodMP.World.Places
{
    /// The host's LocationSlot: where a location stands, by its name.
    public readonly struct LocationSlot
    {
        public string Name { get; init; }

        public Spot Spot { get; init; }
    }
}
