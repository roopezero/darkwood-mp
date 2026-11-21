namespace DarkwoodMP.Sync.Players
{
    /// What a game does with a PlayerState that arrived, in this order:
    /// the move check, the relay, then the puppet unless dropped.
    public readonly struct StateArrival
    {
        /// Whom it's about: the sender on the host (never the owner a
        /// client says), the owner it says on a client.
        public ulong Owner { get; init; }

        public StateMove Move { get; init; }

        /// The host sends it on to everyone but the sender (State,
        /// unreliable), with the owner and the checked position.
        public bool Relays { get; init; }

        /// Not shown when this isn't None.
        public StateDrop Drop { get; init; }
    }
}
