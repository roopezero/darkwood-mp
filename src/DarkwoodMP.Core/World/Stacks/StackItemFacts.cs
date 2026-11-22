namespace DarkwoodMP.World.Stacks
{
    /// What decides whether an item prefab stacks by amount: its InvItem
    /// flags and one of its stack sizes.
    public readonly struct StackItemFacts
    {
        /// InvItem.stackable.
        public bool Stackable { get; init; }

        /// InvItem.stacksDurability: its amount is its fuel (gasoline, the
        /// lantern).
        public bool StacksFuel { get; init; }

        /// InvItem.hasAmmo: its amount is its loaded rounds.
        public bool HoldsAmmo { get; init; }

        /// The stack size it is judged on: the game's own (the plugin's
        /// StackSizes.Original) when the sizes are set, and the prefab's
        /// maxAmount now in Fits.
        public int Max { get; init; }
    }
}
