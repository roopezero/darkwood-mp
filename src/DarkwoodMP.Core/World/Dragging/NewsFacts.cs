namespace DarkwoodMP.World.Dragging
{
    /// A DragOwner heard (DragSync.OnDragOwner).
    public readonly struct NewsFacts
    {
        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// It came from the host (Session.HostId).
        public bool FromHost { get; init; }

        /// The world is loaded (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// Who drags it now, 0 for nobody.
        public ulong Owner { get; init; }

        /// Its let-go flag.
        public bool LetGo { get; init; }

        /// Our player drags that very thing (DragSync._dragging, its ref).
        public bool Ours { get; init; }

        /// This game's player (Session.LocalId).
        public ulong Local { get; init; }
    }
}
