namespace DarkwoodMP.Combat.Enemies
{
    /// What the host's loop check sends the clients that know an enemy
    /// (EnemyLoopRules.Check).
    public enum LoopSend
    {
        /// Nothing.
        None = 0,

        /// The same loop again, as an unreliable EnemySound.
        Again,

        /// A new loop (or "" for none), as a reliable EnemySound.
        Changed,
    }
}
