namespace DarkwoodMP.World.Inventories
{
    /// One slot change the host refused, as sent back to the client
    /// (ContainerSlotRefused).
    public readonly struct RefusedSlot
    {
        /// The slot's number.
        public int Slot { get; init; }

        /// What the slot holds in the host's world (JSON, "" empty).
        public string Held { get; init; }

        /// The key the client saw in the slot.
        public string Old { get; init; }

        /// What the client put there (JSON, "" empty).
        public string Tried { get; init; }
    }
}
