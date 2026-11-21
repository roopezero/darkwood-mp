namespace DarkwoodMP.Combat.Enemies
{
    /// What the client does with an EnemyDespawn (CopyRules.Despawn).
    public enum DespawnStep
    {
        /// Nothing.
        Ignore = 0,

        /// A kept corpse the host has no more, whose character is gone
        /// here too: only forgotten.
        ForgetCorpse,

        /// A kept corpse the host has no more: forgotten, its window
        /// closed, destroyed (our copy from the save too).
        DestroyCorpse,

        /// A copy whose character is gone here: only forgotten.
        ForgetCopy,

        /// A copy left where it fell: settled if it wasn't, kept as a
        /// corpse, back in our culling.
        KeepCorpse,

        /// A copy the host stopped sending: its sounds stopped, then an
        /// NPC that HostileNpcs.ClientStopped takes is ours to run again;
        /// anything else goes by CopyRules.Release.
        Release,
    }
}
