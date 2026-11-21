namespace DarkwoodMP.Combat.Landings
{
    /// Something one of the host's enemies threw, as its clients see it fly
    /// (EnemyProjectileMessage, EnemySensorThrownMessage).
    public readonly struct EnemyThrow
    {
        /// The net id of the enemy that threw it.
        public uint By { get; init; }

        /// The pooled projectile's prefab, or the ranged sensor's name.
        public string Name { get; init; }

        public Vec3 At { get; init; }

        /// The thrown thing's y rotation, in degrees.
        public float Yaw { get; init; }

        /// Its Rigidbody's velocity (zero with none).
        public Vec3 Velocity { get; init; }
    }
}
