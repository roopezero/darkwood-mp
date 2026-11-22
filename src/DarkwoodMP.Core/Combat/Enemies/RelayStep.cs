namespace DarkwoodMP.Combat.Enemies
{
    /// What one client is sent about one tracked enemy in a state pass
    /// (EnemyRelayRules.Choose).
    public enum RelayStep
    {
        /// Nothing.
        Skip = 0,

        /// A corpse it never saw settle: spawn, one dead state, despawn
        /// keeping the corpse, then its loot; remembered as kept.
        SendCorpse,

        /// Dropped after the pass: EnemyDespawn, corpse not kept.
        Drop,

        /// Dropped after the pass: EnemyDespawn keeping the corpse.
        DropCorpse,

        /// New to it: EnemySpawn now, then its state in the batch.
        Spawn,

        /// Its state in the batch.
        State,
    }
}
