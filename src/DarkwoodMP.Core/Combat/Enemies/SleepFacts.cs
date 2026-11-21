namespace DarkwoodMP.Combat.Enemies
{
    /// What the client looks at when the host's state says a living copy
    /// sleeps or doesn't (the plugin's NetEnemies.OnState).
    public readonly struct SleepFacts
    {
        /// The state says it's asleep.
        public bool Asleep { get; init; }

        /// It has an idle AudioObject and it plays.
        public bool Playing { get; init; }

        /// That AudioObject's audioID is its sleeping loop.
        public bool SleepingLoop { get; init; }
    }
}
