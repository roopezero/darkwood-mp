namespace DarkwoodMP.Combat.Damage
{
    /// A character's effects starting in a client's game
    /// (CharacterEffectsActivateOnCopy).
    public readonly struct HostEffectsFacts
    {
        /// NetEnemies.ClientMode and a session (CoopSession.Enemies) exists.
        public bool Handled { get; init; }

        /// The CharacterEffects has a Character.
        public bool Character { get; init; }

        /// NetEnemies.IsPuppet of it.
        public bool Puppet { get; init; }

        /// NetEnemies.Frozen holds it.
        public bool Frozen { get; init; }

        /// HostileNpcs.EffectsForwarded of it.
        public bool Forwarded { get; init; }
    }
}
