namespace DarkwoodMP.World.Hideout
{
    /// Player.setExperienceMachine(machine, doEnable) is about to run.
    public readonly struct OvenCallFacts
    {
        /// The machine isn't null.
        public bool HasOven { get; init; }

        /// doEnable.
        public bool Enable { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }

        /// Playing together (TogetherRules.Together).
        public bool Together { get; init; }
    }
}
