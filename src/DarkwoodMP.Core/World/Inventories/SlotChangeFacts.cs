namespace DarkwoodMP.World.Inventories
{
    /// One change of a client's batch, as the host sees it
    /// (SlotShare.OnSlots).
    public readonly struct SlotChangeFacts
    {
        /// The slot's number, as sent.
        public int Slot { get; init; }

        /// The key the client saw in the slot, as sent.
        public string Old { get; init; }

        /// The slot's key in the host's world now. Not read for a slot past
        /// the container's count.
        public string HostKey { get; init; }

        /// The item in the host's slot now.
        public SlotItem Host { get; init; }

        /// The new item fits in one stack (StackSizes.Fits).
        public bool Fits { get; init; }
    }
}
