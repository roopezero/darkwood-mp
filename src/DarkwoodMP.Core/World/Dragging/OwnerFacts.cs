namespace DarkwoodMP.World.Dragging
{
    /// What a game knows about who drags one thing: its entry in the owner
    /// table (DragSync._owners), decided on the host and as the host said
    /// on a client.
    public readonly struct OwnerFacts
    {
        /// The thing's name is valid (ObjRef.Valid).
        public bool Named { get; init; }

        /// The owner table has an entry for the thing.
        public bool Owned { get; init; }

        /// Who drags it (Owner.Id), when Owned.
        public ulong Owner { get; init; }

        /// This game's player (Session.LocalId).
        public ulong Local { get; init; }

        /// When the owner was last heard of (Owner.Heard).
        public Seconds Heard { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }
    }
}
