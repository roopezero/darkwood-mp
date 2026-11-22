namespace DarkwoodMP.World.Mornings
{
    /// The clock's new state and what to do to the game's clock.
    public readonly struct ClockStep
    {
        public ClockState Clock { get; init; }

        public ClockChange Change { get; init; }
    }
}
