namespace DarkwoodMP.World.Skills
{
    /// One of the game's skill tiers (SkillTiers.Tier), by the names of
    /// its skills. The shell leaves out the null entries of Tier.skills and
    /// Tier.negativeSkills, which the plugin's code skips everywhere.
    public readonly struct TierFacts
    {
        /// The level the tier opens at (Tier.level).
        public int Level { get; init; }

        /// The names of its good skills, in the game's order.
        public string[] Good { get; init; }

        /// The names of its negative skills, in the game's order.
        public string[] Negative { get; init; }
    }
}
