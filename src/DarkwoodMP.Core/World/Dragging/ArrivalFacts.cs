namespace DarkwoodMP.World.Dragging
{
    /// An ObjMove that has just come in (DragSync.OnMove).
    public readonly struct ArrivalFacts
    {
        /// The world is loaded (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// It came from the host (Session.HostId).
        public bool FromHost { get; init; }
    }
}
