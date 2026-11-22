using System.Collections.Generic;

namespace DarkwoodMP.World.Skills
{
    /// A client's SkillSpend: the choice it learned, at its level, with the
    /// tier skills it had before.
    public readonly struct SpendRequest
    {
        /// Player.currentLevel; sent as one byte, 0 to 255.
        public int Level { get; init; }

        /// The good skill learned.
        public string Good { get; init; }

        /// The negative skill taken with it; "" or null for none, which
        /// is sent as "".
        public string Negative { get; init; }

        /// The tier skills had before (SkillTree.Chosen); at most 255 are
        /// sent. Null counts as none.
        public IList<string> Had { get; init; }
    }
}
