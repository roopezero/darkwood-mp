namespace DarkwoodMP.Combat.Damage
{
    /// An explosion and one stand-in (EnemyAreaHits.Explosion).
    public readonly struct BlastFacts
    {
        /// Explodes.radius.
        public float Radius { get; init; }

        /// Explodes.damage.
        public float Damage { get; init; }

        /// Core.trueDistance from the stand-in to the explosion.
        public float Distance { get; init; }

        /// Core.canSee(the explosion, the stand-in). The game reads it
        /// only when the distance is under the radius, and so may the
        /// shell.
        public bool Sees { get; init; }
    }
}
