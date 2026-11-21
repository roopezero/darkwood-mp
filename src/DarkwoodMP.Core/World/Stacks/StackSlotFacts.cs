namespace DarkwoodMP.World.Stacks
{
    /// One item in one container slot, for Fits.
    public readonly struct StackSlotFacts
    {
        /// There is an item: not InvItemClass.isNull, and it has a
        /// baseClass (its prefab).
        public bool Exists { get; init; }

        /// The prefab's flags, with Max its maxAmount now.
        public StackItemFacts Item { get; init; }

        /// InvItemClass.amount.
        public int Amount { get; init; }
    }
}
