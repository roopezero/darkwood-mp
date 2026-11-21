namespace DarkwoodMP.World.Inventories
{
    /// A stack after Sort & Stack: the slot it came from, with its new
    /// amount and durability. Its place in SortRules.Sort's list is its new
    /// slot.
    public readonly struct SortedStack
    {
        /// The slot it was in before the sort.
        public int From { get; init; }

        public int Amount { get; init; }

        public float Durability { get; init; }
    }
}
