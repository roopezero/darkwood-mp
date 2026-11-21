namespace DarkwoodMP.World.Inventories
{
    /// One slot change as sent (ContainerSlots): slot Slot held what key
    /// Old names, and holds New now (InvItemClass.SaveState as JSON, ""
    /// when empty).
    public readonly struct SlotChange
    {
        public int Slot { get; init; }

        public string Old { get; init; }

        public string New { get; init; }
    }
}
