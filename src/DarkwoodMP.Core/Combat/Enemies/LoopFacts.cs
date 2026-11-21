namespace DarkwoodMP.Combat.Enemies
{
    /// What the host's loop check looks at for one tracked enemy (the
    /// plugin's NetEnemies.LoopCheck).
    public readonly struct LoopFacts
    {
        /// The enemy still exists.
        public bool Exists { get; init; }

        /// Its death was timed (Tracked.DiedAt isn't negative).
        public bool Dead { get; init; }

        /// EnemyTrackRules.InPlay of it.
        public bool InPlay { get; init; }

        /// The loop it plays now (EnemyLoopRules.CurrentLoop).
        public string Current { get; init; }

        /// EnemyLoopRules.IsLoop of Current.
        public bool IsLoop { get; init; }

        /// The loop the clients were told last (Tracked.Loop, "" at
        /// first).
        public string Told { get; init; }

        /// Every client is told its loop again in this check (the 2 s
        /// resend is due).
        public bool Resend { get; init; }
    }
}
