namespace DarkwoodMP.World.Dragging
{
    /// What our game knows when it applies an ObjMove.
    public readonly struct ApplyFacts
    {
        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// The thing is here (ObjRef.Resolve of an Item).
        public bool Found { get; init; }

        /// The move is where it came to rest (the message's last).
        public bool Last { get; init; }

        /// Its name is a Spawned one (ObjRef.Kind.Spawned).
        public bool Spawned { get; init; }

        /// The owner table has an entry for it.
        public bool Owned { get; init; }

        /// Our player drags it (DragSync._dragging).
        public bool Ours { get; init; }
    }
}
