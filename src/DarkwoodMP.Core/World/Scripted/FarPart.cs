namespace DarkwoodMP.World.Scripted
{
    /// The part a location's enter or exit events run in, and the host's
    /// record. The zero value: as the game runs them, record kept.
    public readonly struct FarPart
    {
        /// The events run in Part; otherwise as the game runs them.
        public bool Set { get; init; }

        public EventPart Part { get; init; }

        public FarRecord Record { get; init; }

        /// What to store, with FarRecord.Store.
        public bool HostIn { get; init; }
    }
}
