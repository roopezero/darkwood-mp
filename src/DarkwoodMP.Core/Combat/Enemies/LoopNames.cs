namespace DarkwoodMP.Combat.Enemies
{
    /// The six loops CharacterSounds.playIdleLoop plays for a character
    /// (its CharacterSounds' fields of those names).
    public readonly struct LoopNames
    {
        /// The character has a CharacterSounds; without one nothing is a
        /// loop.
        public bool Exists { get; init; }

        public string Idle { get; init; }

        /// CharacterSounds.idleLoopAggressive.
        public string IdleAggressive { get; init; }

        public string Aggressive { get; init; }

        public string Defensive { get; init; }

        public string Sleeping { get; init; }

        /// CharacterSounds.escapingLoop.
        public string Escaping { get; init; }
    }
}
