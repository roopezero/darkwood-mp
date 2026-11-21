namespace DarkwoodMP.World.Inventories
{
    /// Something made while playing, as ObjSpawnMessage carries it.
    public readonly struct SpawnedThing
    {
        /// The random id its maker gave it (ObjRef.Spawned).
        public long Id { get; init; }

        /// A player placed it (flag 1).
        public bool Placed { get; init; }

        /// The prefab's path under Resources/Prefabs.
        public string Prefab { get; init; }

        public Vec3 Pos { get; init; }

        public Quat Rot { get; init; }

        /// What's in it (Inventory.SaveState as JSON), or "".
        public string Contents { get; init; }

        /// A death bag's DeathDrop.expAmount, else 0.
        public int Experience { get; init; }
    }
}
