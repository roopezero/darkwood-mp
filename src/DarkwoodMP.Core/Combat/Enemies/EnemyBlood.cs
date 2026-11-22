namespace DarkwoodMP.Combat.Enemies
{
    /// Blood from a hit on the host's enemy, as EnemyBlood carries it.
    public readonly struct EnemyBlood
    {
        public uint Id { get; init; }

        /// The splat's rotation about Y, in degrees.
        public float RotY { get; init; }

        /// The enemy stood in water.
        public bool Water { get; init; }
    }
}
