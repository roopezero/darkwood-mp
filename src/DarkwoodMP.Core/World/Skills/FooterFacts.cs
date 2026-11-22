namespace DarkwoodMP.World.Skills
{
    /// What the Skills panel's footer line looks at.
    public readonly struct FooterFacts
    {
        /// Why the last Learn didn't go is still shown (SkillsWindow's
        /// _message isn't null).
        public bool HasMessage { get; init; }

        /// A choice is waiting for the host's answer (SkillTree.Waiting).
        public bool Waiting { get; init; }

        /// The good skill picked; null for none.
        public string Good { get; init; }

        /// The negative skill picked; null for none.
        public string Negative { get; init; }

        /// SkillRules.Points for our player.
        public int Points { get; init; }

        /// SkillRules.NeedsNegative for the good skill picked.
        public bool NeedsNegative { get; init; }
    }
}
