namespace DarkwoodMP.Combat.Effects
{
    /// What one EnemyEffect says (EnemyEffectMessage). An emitter (kind 3)
    /// has Id and Toggle; any other kind has Name and At, and a prefab
    /// (kind 1) also RotY.
    public readonly struct EnemyEffect
    {
        public byte Kind { get; init; }

        /// The enemy's net ID (an emitter).
        public uint Id { get; init; }

        /// Character.modifyEmitter's toggle (an emitter).
        public int Toggle { get; init; }

        /// The prefab's name.
        public string Name { get; init; }

        public Vec3 At { get; init; }

        /// The prefab's rotation about Y (kind 1 only).
        public float RotY { get; init; }
    }
}
