namespace DarkwoodMP.World.Skills
{
    /// The good and negative skill picked on the Skills panel, not learned
    /// yet; null for none.
    public readonly struct SkillPicks
    {
        public string Good { get; init; }

        public string Negative { get; init; }
    }
}
