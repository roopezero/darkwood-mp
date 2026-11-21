namespace DarkwoodMP.World.Rolls
{
    /// The game's own gate on a RandomEvent.fire(checkIfRequirementsMet,
    /// force) call, which returns before doing anything when it's shut
    /// (AmbushRules.GameWants).
    public readonly struct AmbushGateFacts
    {
        /// RandomEvent.disabled.
        public bool Disabled { get; init; }

        /// The force argument.
        public bool Force { get; init; }

        /// The checkIfRequirementsMet argument.
        public bool CheckRequirements { get; init; }

        /// RandomEvent.requirementsMet(), read only when
        /// AmbushRules.AsksRequirements says so.
        public bool RequirementsMet { get; init; }

        /// RandomEvent.startedToday.
        public bool StartedToday { get; init; }

        /// RandomEvent.multipleTimesPerDay.
        public bool MultipleTimesPerDay { get; init; }
    }
}
