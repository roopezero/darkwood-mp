namespace DarkwoodMP.Combat.Damage
{
    /// Host: the blood the game splats for a hit on one of its characters,
    /// shown to the clients too (EnemyBlood, sent by NetEnemies.HostBlood).
    public static class BloodRules
    {
        /// The health the hit took.
        public static float Dealt(in BloodFacts hit) => hit.Before - hit.After;

        /// A normal hit that took health, not on the night trader, and we
        /// host.
        public static bool Sends(in BloodFacts hit) =>
            hit.NormalHit
            && Dealt(hit) > 0f
            && !hit.NightTrader
            && hit.Host;

        /// The blood's rotation: the attacker's (its rotation turned by
        /// Euler(90, 180, 0), the y the shell reads) when there is one,
        /// else the character's own y.
        public static float Rotation(bool attacker, float turned, float own)
        {
            if (attacker)
            {
                return turned;
            }

            return own;
        }
    }
}
