namespace DarkwoodMP.Combat.Enemies
{
    /// What the client looks at for an EnemySound (the plugin's
    /// NetEnemies.OnSound).
    public readonly struct SoundFacts
    {
        /// The id is a copy we show and its character exists.
        public bool Copy { get; init; }

        /// It died here (Puppet.DiedAt isn't negative).
        public bool Dead { get; init; }

        /// It has a CharacterSounds.
        public bool Sounds { get; init; }

        /// The sound is its death cry (CharacterSounds.death).
        public bool DeathCry { get; init; }

        /// The sound's audio id is null or "".
        public bool Empty { get; init; }

        /// EnemyLoopRules.IsLoop of the sound.
        public bool IsLoop { get; init; }
    }
}
