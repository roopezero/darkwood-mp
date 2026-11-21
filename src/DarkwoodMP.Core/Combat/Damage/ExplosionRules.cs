namespace DarkwoodMP.Combat.Damage
{
    /// Host: what an explosion in its world does to remote players'
    /// stand-ins, and whether the clients see it (EnemyAreaHits.Explosion
    /// and SendExplosion). Every explosion counts, the host player's own
    /// bombs included: a client's game doesn't hurt its player with them.
    public static class ExplosionRules
    {
        /// Whether the explosion is looked at at all: it has an effect
        /// (one with none neither hurts stand-ins nor is sent) and we host
        /// (AreaHitRules.Hosting).
        public static bool Handles(bool effect, bool hosting) =>
            effect && hosting;

        /// Whether it can hurt a stand-in: affectsPlayer and a radius
        /// above 0.
        public static bool HurtsStandIns(bool affects, float radius) =>
            affects && radius > 0f;

        /// The hit on one stand-in: none at or past the radius or out of
        /// sight; else damage * (radius - d) / radius, none at 0 or less.
        public static bool Hits(in BlastFacts blast, out float hit)
        {
            hit = 0f;
            if (blast.Distance >= blast.Radius || !blast.Sees)
            {
                return false;
            }

            float damage = blast.Damage
                * ((blast.Radius - blast.Distance) / blast.Radius);
            if (damage <= 0f)
            {
                return false;
            }

            hit = damage;
            return true;
        }

        /// Whether EnemyExplosion goes to every client: one of the host's
        /// enemies made it, we host with peers, and it has a sound or an
        /// effect to show. No distance check: each client checks its own.
        public static bool Sends(in ExplosionSendFacts blast) =>
            blast.ByEnemy
            && blast.Host
            && blast.Peers > 0
            && (blast.Sound || blast.Effect);
    }
}
