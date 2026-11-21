namespace DarkwoodMP.Combat.Enemies
{
    /// What the host's 0.5 s scan looks at for one character it tracks
    /// (the plugin's NetEnemies.HostUpdate).
    public readonly struct CalmFacts
    {
        /// The character still exists (its key isn't destroyed).
        public bool Exists { get; init; }

        /// It's an NPC (Character.npc isn't null).
        public bool Npc { get; init; }

        /// EnemyTrackRules.InPlay of it.
        public bool InPlay { get; init; }

        /// HostileNpcs.StillHostile of it.
        public bool Hostile { get; init; }

        /// Its calm timer runs (Tracked.CalmSince isn't negative).
        public bool Calming { get; init; }

        /// When it calmed down (Tracked.CalmSince), when Calming.
        public Seconds CalmSince { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }
    }
}
