namespace DarkwoodMP.World.Inventories
{
    /// What a client knows about one slot of a container it watches when
    /// the host's contents for it come (SlotShare.TakeHost, Merge,
    /// OnRefused).
    public readonly struct SlotFacts
    {
        /// We have a key for the slot from our last look (the container is
        /// watched, and the slot is within what we saw and here).
        public bool Seen { get; init; }

        /// The slot's key at our last look, when Seen.
        public string SeenKey { get; init; }

        /// The slot's key here now.
        public string NowKey { get; init; }

        /// The key of what the host says the slot holds.
        public string HostKey { get; init; }

        /// The item in the slot here now.
        public SlotItem Here { get; init; }
    }
}
