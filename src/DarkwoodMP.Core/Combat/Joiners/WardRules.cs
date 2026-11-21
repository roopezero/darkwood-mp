namespace DarkwoodMP.Combat.Joiners
{
    /// Wards and forest skills that the game reads from the host's player
    /// read from the enemy's target instead (the plugin's Wards).
    public static class WardRules
    {
        /// Whether the enemy's target counts as a stand-in: the host has
        /// stand-ins, there is an enemy with a target, and the target has
        /// a NetPlayerProxy.
        public static bool TargetsStandIn(
            int standIns,
            bool target,
            bool standIn) =>
            standIns != 0 && target && standIn;

        /// Whether the target has the effect: a stand-in has only its two
        /// wards, whether its player is alive or away; else the host's
        /// player's effects say.
        public static bool HasEffect(in WardFacts ward)
        {
            if (!ward.StandIn)
            {
                return ward.HostHas;
            }

            if (ward.Kind == WardKind.ShadowWard)
            {
                return ward.ShadowWard;
            }

            if (ward.Kind == WardKind.ForestSpiritWard)
            {
                return ward.ForestSpiritWard;
            }

            return false;
        }

        /// A forest skill (FriendOfTheForest, EnemyOfTheForest) of the
        /// target: the stand-in's, else the host's player's.
        public static bool SkillOf(
            bool standIn,
            bool standInHas,
            bool hostHas)
        {
            if (standIn)
            {
                return standInHas;
            }

            return hostHas;
        }
    }
}
