namespace DarkwoodMP.Combat.Damage
{
    /// An enemy's melee hit on a remote player's stand-in, with the checks
    /// the game makes for a player (MeleeSensorProxyPatch).
    public static class MeleeRules
    {
        /// Checked in this order: a stand-in, an enemy's sensor with an
        /// attacker, not hit before, then the attacker's sight.
        public static MeleeOutcome Touch(in MeleeFacts touch)
        {
            if (!touch.StandIn)
            {
                return MeleeOutcome.Game;
            }

            if (!touch.EnemySensor || !touch.Attacker)
            {
                return MeleeOutcome.NotAnEnemyHit;
            }

            if (touch.AlreadyHit)
            {
                return MeleeOutcome.AlreadyHit;
            }

            if (!touch.NoSightNeeded && !touch.Sees)
            {
                return MeleeOutcome.Unseen;
            }

            return MeleeOutcome.Hits;
        }

        /// The sensor's damage times the attacker's strength modifier (1
        /// with no CharBase).
        public static float Damage(in MeleeFacts touch)
        {
            if (touch.Strength)
            {
                return touch.Damage * touch.Modifier;
            }

            return touch.Damage * 1f;
        }
    }
}
