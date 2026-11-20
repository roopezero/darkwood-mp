namespace DarkwoodMP.World.KeptCharacters
{
    /// One item of a bag a client sent (Inventory.SaveState.invItems): its
    /// type (null for a null item) and amount.
    public readonly struct CarriedItem
    {
        public string Type { get; init; }
        public int Amount { get; init; }
    }
}
