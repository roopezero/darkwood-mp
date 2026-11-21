namespace DarkwoodMP.Combat.Damage
{
    /// A flame touching something on the host (EnemyAreaHits.FlameTouch,
    /// called from ThrowSync's Flame.onCollideWith patch).
    public readonly struct FlameTouchFacts
    {
        /// The flame isn't null and isActive.
        public bool Active { get; init; }

        /// AreaHitRules.Hosting.
        public bool Hosting { get; init; }

        /// A NetPlayerProxy on the collider's transform, or else on the
        /// touched transform.
        public bool StandIn { get; init; }

        /// That stand-in is alive.
        public bool Alive { get; init; }

        /// EnemyAreaHits.ByEnemy(the flame's transform).
        public bool ByEnemy { get; init; }
    }
}
