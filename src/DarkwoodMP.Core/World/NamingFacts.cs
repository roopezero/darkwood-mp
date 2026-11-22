namespace DarkwoodMP.World
{
    /// What ObjRef.Of looks at to name an object.
    public readonly struct NamingFacts
    {
        /// The object is alive.
        public bool Exists { get; init; }

        /// Made while playing together (ObjRef.RegisterSpawned), and its id.
        public bool Spawned { get; init; }

        public long SpawnedId { get; init; }

        /// A Character the enemies' table knows (NetEnemies.NetIdOf).
        public bool Enemy { get; init; }

        public uint EnemyId { get; init; }

        /// Its SaveableObject.uniqueId; 0 when it has none.
        public int SaveId { get; init; }

        /// SharedIdRules.SendLimit.
        public int SendLimit { get; init; }

        /// Tied to an id the host gave out (HostIds), and that id.
        public bool Shared { get; init; }

        public long SharedId { get; init; }

        /// Pinned before it moved (ObjRef.Pin), and its kept key.
        public bool Pinned { get; init; }

        public long PinnedKey { get; init; }

        /// StableHash.Of its name, and where it stands.
        public int NameHash { get; init; }

        public Vec2 Pos { get; init; }
    }
}
