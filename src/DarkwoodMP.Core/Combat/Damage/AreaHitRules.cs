namespace DarkwoodMP.Combat.Damage
{
    /// Host: enemy damage that comes through no melee sensor or bullet,
    /// for remote players' stand-ins, with the game's own falloff
    /// (EnemyAreaHits; explosions are ExplosionRules'). Distances are the
    /// shell's Core.trueDistance.
    public static class AreaHitRules
    {
        /// Above this many objects a spawn set drops its destroyed ones.
        public const int MostSpawned = 512;

        /// The damage of a flier's dive (the game's literal in
        /// Flier.Update).
        public const float DiveDamage = 5f;

        /// The least distance a shot's damage is divided by.
        public const float LeastShotDistance = 1f;

        /// Whether the host's world hurts stand-ins: there are stand-ins
        /// (Proxies.Any), and we aren't a client (alone counts, which the
        /// dummy needs).
        public static bool Hosting(bool standIns, bool client) =>
            standIns && !client;

        /// Which sets a spawned object goes in: the enemy set inside an
        /// enemy's spawn while hosting, the player set inside our player's
        /// bullet spawn (with no session too).
        public static SpawnSets Sets(
            int enemyDepth, int playerDepth, bool hosting)
        {
            var sets = SpawnSets.None;
            if (enemyDepth > 0 && hosting)
            {
                sets |= SpawnSets.Enemy;
            }

            if (playerDepth > 0)
            {
                sets |= SpawnSets.Player;
            }

            return sets;
        }

        /// Whether a set drops its destroyed objects before an add.
        public static bool Prunes(int count) => count > MostSpawned;

        /// Whether one of the host's enemies spawned it: its spawner is a
        /// Character NetEnemies runs (checked first), or it or a parent
        /// is in the enemy set.
        public static bool ByEnemy(bool enemySpawner, bool inEnemySet) =>
            enemySpawner || inEnemySet;

        /// Whether a stand-in can be hurt: alive, and its collider (if it
        /// has one) is enabled.
        public static bool Living(bool alive, bool collider, bool enabled) =>
            alive && (!collider || enabled);

        /// Whether a damage aura runs at all: the character is alive, we
        /// host, and its range isn't 0 or less (a NaN range runs).
        public static bool AuraRuns(bool alive, bool hosting, float range) =>
            alive && hosting && !(range <= 0f);

        /// The aura's hit on a stand-in at that distance: none past the
        /// range; else damage * (range / 2 - d) / range, sent only when
        /// that factor is above 0 (so only within half the range).
        public static bool AuraHit(
            float damage, float range, float d, out float hit)
        {
            hit = 0f;
            if (d > range)
            {
                return false;
            }

            float f = (range / 2f - d) / range;
            if (!(f > 0f))
            {
                return false;
            }

            hit = damage * f;
            return true;
        }

        /// Whom a diving flier hits: its target's stand-in, else our own
        /// player if there is one (Player.Instance; alone too).
        public static FlierTarget Dive(bool standIn, bool player)
        {
            if (standIn)
            {
                return FlierTarget.StandIn;
            }

            if (player)
            {
                return FlierTarget.OurPlayer;
            }

            return FlierTarget.Nobody;
        }

        /// Whether a Shooter's shot hurts stand-ins: it has a target, we
        /// host, and it is on one of the host's enemies (NetEnemies
        /// .Eligible: a Shooter of the world's own runs in every game).
        public static bool Shoots(bool target, bool hosting, bool enemy) =>
            target && hosting && enemy;

        /// A shot's hit on a stand-in: every stand-in it can see, not only
        /// its target, for damage / max(1, d).
        public static bool ShotHit(
            float damage, float d, bool sees, out float hit)
        {
            hit = 0f;
            if (!sees)
            {
                return false;
            }

            hit = damage / Floats.Max(LeastShotDistance, d);
            return true;
        }

        /// Whether a flame touching a stand-in hurts its player (for the
        /// flame's contactDamage, with its effect).
        public static bool FlameHurts(in FlameTouchFacts touch) =>
            touch.Active
            && touch.Hosting
            && touch.StandIn
            && touch.Alive
            && touch.ByEnemy;

        /// Client: whether the game's Flier.Update or Shooter.shoot is
        /// skipped: a frozen copy of the host's enemy doesn't dive or
        /// shoot.
        public static bool Frozen(bool client, bool frozen) =>
            client && frozen;
    }
}
