namespace DarkwoodMP.World.Dragging
{
    /// Whether our drag's next move goes out (DragSync.Update).
    public readonly struct SendFacts
    {
        /// Our player drags something.
        public bool Dragging { get; init; }

        /// Its name is valid (ObjRef.Valid).
        public bool Named { get; init; }

        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        /// Client: the host agreed that our player drags it.
        public bool Claimed { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// When the next move is due (_nextSend; 0 sends at once).
        public Seconds Next { get; init; }
    }
}
