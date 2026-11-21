namespace DarkwoodMP.Combat.Enemies
{
    /// A copy's health set to the host's (CopyStateRules.Health). Zero is
    /// no change.
    public readonly struct HealthChange
    {
        /// The health changes.
        public bool Changes { get; init; }

        /// What it becomes, when it changes.
        public float Health { get; init; }

        /// It went down: the health bar shows if our player has it
        /// selected (else an open bar is refreshed).
        public bool Lost { get; init; }
    }
}
