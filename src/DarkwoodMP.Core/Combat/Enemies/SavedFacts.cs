namespace DarkwoodMP.Combat.Enemies
{
    /// What the client looks at to show the host's enemy with its own
    /// copy from the save (the plugin's NetEnemies.OnSpawn).
    public readonly struct SavedFacts
    {
        /// The save id the host sent (EnemySpawn's uid).
        public int SaveId { get; init; }

        /// SharedIdRules.ResolveLimit: ids under it are the same in both
        /// games.
        public int Limit { get; init; }

        /// SaveManager exists and its uniqueIdDict has a live transform
        /// for SaveId.
        public bool Found { get; init; }

        /// That transform has a Character.
        public bool HasCharacter { get; init; }

        /// That character already shows another of the host's enemies
        /// (_byChar holds it).
        public bool Driven { get; init; }

        /// Its kind (EnemyTrackRules.KindOf).
        public string Kind { get; init; }

        /// The kind the host sent (EnemySpawn's name).
        public string HostKind { get; init; }
    }
}
