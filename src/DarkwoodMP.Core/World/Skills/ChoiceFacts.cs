using System.Collections.Generic;

namespace DarkwoodMP.World.Skills
{
    /// A player's choice of skills: the game's tiers, the player's level and
    /// the skills they have, and the good and negative skill picked.
    public readonly struct ChoiceFacts
    {
        /// The game's tiers (SkillTree.Tiers); null before the world is up.
        public TierFacts[] Tiers { get; init; }

        /// Player.currentLevel, or the level a client sent.
        public int Level { get; init; }

        /// The names of the tier skills they have (SkillTree.Chosen), or the
        /// list a client sent. Null counts as none.
        public HashSet<string> Chosen { get; init; }

        /// The good skill picked, by name.
        public string Good { get; init; }

        /// The negative skill picked, by name; null or "" for none.
        public string Negative { get; init; }
    }
}
