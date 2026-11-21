using System.Collections.Generic;

namespace DarkwoodMP.World.Skills
{
    /// A click on a skill's cell in the Skills panel, and what was picked
    /// before it.
    public readonly struct PickFacts
    {
        /// The game's tiers.
        public TierFacts[] Tiers { get; init; }

        /// The skills our player has; null when there's no player, which
        /// skips the last check (SkillsPanel's p != null).
        public HashSet<string> Chosen { get; init; }

        /// The name of the skill clicked.
        public string Clicked { get; init; }

        /// The cell clicked is in the negative skills' rows.
        public bool ClickedNegative { get; init; }

        /// The good skill picked before the click; null for none.
        public string Good { get; init; }

        /// The negative skill picked before the click; null for none.
        public string Negative { get; init; }
    }
}
