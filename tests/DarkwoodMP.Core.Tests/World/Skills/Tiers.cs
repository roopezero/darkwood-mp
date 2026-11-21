using System.Collections.Generic;
using DarkwoodMP.World.Skills;

namespace DarkwoodMP.Tests.World.Skills
{
    /// Made-up skill tiers for the tests, shaped like the game's: tier I
    /// opens at level 1 with two negative skills, tier II at level 3 with
    /// one, tier III at level 5 with none.
    static class Tiers
    {
        public static readonly TierFacts[] Three =
        {
            new TierFacts
            {
                Level = 1,
                Good = new[] { "a1", "a2", "a3" },
                Negative = new[] { "n1", "n2" },
            },
            new TierFacts
            {
                Level = 3,
                Good = new[] { "b1", "b2", "b3" },
                Negative = new[] { "m1" },
            },
            new TierFacts
            {
                Level = 5,
                Good = new[] { "c1", "c2" },
                Negative = new string[0],
            },
        };

        public static HashSet<string> Set(params string[] names)
        {
            return new HashSet<string>(names);
        }

        /// Level 1, nothing had, a1 picked with n1: allowed.
        public static readonly ChoiceFacts First = new ChoiceFacts
        {
            Tiers = Three,
            Level = 1,
            Chosen = Set(),
            Good = "a1",
            Negative = "n1",
        };
    }
}
