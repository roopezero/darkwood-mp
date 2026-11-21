namespace DarkwoodMP.Combat.Landings
{
    /// What one of the host's enemies' projectiles left where it landed
    /// (EnemyLandedMessage).
    public readonly struct EnemyLanding
    {
        /// Its spawned id (ObjRef Spawned).
        public long Id { get; init; }

        /// The net id of the enemy that threw it.
        public uint By { get; init; }

        /// The prefab it was made of (ThrownItem.prefabToSpawnOnLand).
        public string Name { get; init; }

        public Vec3 At { get; init; }

        /// Its y rotation, in degrees.
        public float Yaw { get; init; }
    }
}
