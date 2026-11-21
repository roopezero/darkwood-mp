namespace DarkwoodMP.Combat.Damage
{
    /// One of the host's enemies' explosions, as its clients see and hear
    /// it (EnemyExplosionMessage).
    public readonly struct EnemyExplosion
    {
        public Vec3 At { get; init; }

        /// The game's audio id, "" for none.
        public string Sound { get; init; }

        /// The explosion prefab's name, "" for none.
        public string Effect { get; init; }
    }
}
