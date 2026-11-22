namespace DarkwoodMP.World.Story
{
    /// PrologueRules.SlotOf's answer.
    public readonly struct SlotAnswer
    {
        public SlotKind Kind { get; init; }

        /// The slot's index, unless Kind is None. For Last it is the new
        /// actualSpawnedLocationsCount.
        public int Index { get; init; }
    }
}
