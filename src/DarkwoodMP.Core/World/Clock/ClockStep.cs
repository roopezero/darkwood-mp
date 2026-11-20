namespace DarkwoodMP.World.Clock
{
    /// What ClockRules.CatchUp decided.
    public readonly struct ClockStep
    {
        public ClockMove Move { get; init; }

        /// For Step: how many minutes, 0 to MaxSteps. 0 otherwise.
        public int Minutes { get; init; }
    }
}
