namespace DarkwoodMP.Combat.Enemies
{
    /// What the client looks at for an EnemyDespawn (the plugin's
    /// NetEnemies.OnDespawn).
    public readonly struct DespawnFacts
    {
        /// The id is a copy we show (_puppets holds it).
        public bool Copy { get; init; }

        /// The id is a corpse we keep (_corpses holds it).
        public bool Corpse { get; init; }

        /// The host says to keep the corpse.
        public bool KeepCorpse { get; init; }

        /// That copy's or corpse's character still exists here.
        public bool Exists { get; init; }
    }
}
