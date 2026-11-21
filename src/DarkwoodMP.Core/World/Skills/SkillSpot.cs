namespace DarkwoodMP.World.Skills
{
    /// Where a skill is in the tiers: the tier's index and whether it is a
    /// negative skill. The zero value is "no such skill".
    public readonly struct SkillSpot
    {
        public static SkillSpot None => default;

        /// The tier's index plus one, so that zero means "not found".
        readonly int _tier;

        public SkillSpot(int tier, bool negative)
        {
            _tier = tier + 1;
            Negative = negative;
        }

        public bool Found => _tier > 0;

        /// The tier's index; -1 when not found.
        public int Tier => _tier - 1;

        public bool Negative { get; }
    }
}
