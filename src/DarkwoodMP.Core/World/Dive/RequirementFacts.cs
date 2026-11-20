namespace DarkwoodMP.World.Dive
{
    /// What an EventTriggerRequirement is, after the game checked it.
    public readonly struct RequirementFacts
    {
        /// type is playerState, and playerState is haveItem.
        public bool HaveItem { get; init; }

        /// Its itemType is the full tank: a GameObject whose InvItem.type
        /// is SharedTankRules.Item.
        public bool Tank { get; init; }

        /// The flag is set (SharedTank.Filled).
        public bool Filled { get; init; }

        /// activeModifier: true for "has a tank", false for its "no tank"
        /// counterpart.
        public bool ActiveModifier { get; init; }

        /// What the game's requirementsMet returned.
        public bool Result { get; init; }
    }
}
