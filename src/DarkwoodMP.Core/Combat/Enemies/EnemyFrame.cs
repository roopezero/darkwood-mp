namespace DarkwoodMP.Combat.Enemies
{
    /// One of the host's enemies in an EnemyState.
    public readonly struct EnemyFrame
    {
        public uint Id { get; init; }

        public Vec3 At { get; init; }

        public Quat Rot { get; init; }

        /// EnemyStateMessage's Alive, Asleep and Cut.
        public byte Flags { get; init; }

        /// Its health as a fraction of its maximum.
        public float Health { get; init; }

        public EnemyAnim[] Anims { get; init; }
    }
}
