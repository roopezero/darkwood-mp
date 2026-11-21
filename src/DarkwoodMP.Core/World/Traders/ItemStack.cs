namespace DarkwoodMP.World.Traders
{
    /// One stack of items: in a trader's inventory (getAllItems), in an
    /// exchange window, or in a stock sent as JSON (Inventory.SaveState's
    /// invItems).
    public readonly struct ItemStack
    {
        /// The item's type; null or empty for an empty stack (a null
        /// entry, or InvItemClass.isNull), which isn't counted.
        public string Type { get; init; }

        public int Amount { get; init; }
    }
}
