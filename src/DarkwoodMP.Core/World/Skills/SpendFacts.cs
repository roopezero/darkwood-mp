namespace DarkwoodMP.World.Skills
{
    /// What the host looks at when a client asks to spend a skill point
    /// (SkillSpend).
    public readonly struct SpendFacts
    {
        /// The host's tiers, with the level, the skills and the choice the
        /// client sent.
        public ChoiceFacts Choice { get; init; }

        /// The most levels a player can have now, in the host's game
        /// (ProgressRules.MostLevels).
        public int Most { get; init; }
    }
}
