namespace DarkwoodMP.World.Joining
{
    /// The map test's hold after one ask: on or not, and its new state.
    public readonly struct HoldStep
    {
        public bool Holding { get; init; }
        public TestHold Hold { get; init; }
    }
}
