namespace DarkwoodMP.World.Skills
{
    /// What the Skills panel's details say about one skill: a note, or a
    /// refusal's words when Note is Refused.
    public readonly struct SkillDetail
    {
        public PanelNote Note { get; init; }

        /// Why it can't be taken; Allow unless Note is Refused.
        public Verdict Verdict { get; init; }
    }
}
