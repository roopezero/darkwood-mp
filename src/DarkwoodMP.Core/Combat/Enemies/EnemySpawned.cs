namespace DarkwoodMP.Combat.Enemies
{
    /// One of the host's enemies, as EnemySpawn carries it.
    public readonly struct EnemySpawned
    {
        /// The host's net id for it.
        public uint Id { get; init; }

        /// Its SaveableObject.uniqueId on the host, 0 for none.
        public int SavedId { get; init; }

        /// The prefab's path under Resources/Prefabs.
        public string Prefab { get; init; }

        /// What kind of character it is (EnemyTrackRules.KindOf).
        public string Kind { get; init; }

        public Vec3 At { get; init; }

        public Quat Rot { get; init; }

        /// It wears shadow armour.
        public bool Armor { get; init; }
    }
}
