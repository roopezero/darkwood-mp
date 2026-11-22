namespace DarkwoodMP.World.Mornings
{
    /// The host's clock during its own morning: whether we let it run, and
    /// since when the morning has been frozen. Zero: not in a morning.
    public readonly struct ClockState
    {
        /// We turned the clock back on (Controller.DoUpdateTime = true).
        public bool Freed { get; init; }

        /// Since is set: the morning's freeze was seen.
        public bool Timing { get; init; }

        /// When the freeze was first seen (unscaled time).
        public Seconds Since { get; init; }
    }
}
