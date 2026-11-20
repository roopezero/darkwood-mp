namespace DarkwoodMP.Combat.Effects
{
    /// What a client does with the host's EnemyEffect (EffectRules.Show).
    public enum EffectShow : byte
    {
        /// Not shown: our player is missing, away or too far.
        NotShown = 0,

        /// Kind 3: the puppet's modifyEmitter, with no distance check.
        Emitter,

        /// Kind 1: Core.AddPrefab with the rotation.
        Prefab,

        /// Any other kind: Core.AddPooledPrefab from the "FX" pool.
        Pooled,
    }
}
