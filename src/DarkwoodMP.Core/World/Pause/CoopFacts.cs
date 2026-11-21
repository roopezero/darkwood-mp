namespace DarkwoodMP.World.Pause
{
    /// What decides whether others share this world right now.
    public readonly struct CoopFacts
    {
        /// There is a session and it is active (Session.Active).
        public bool Active { get; init; }

        /// It is connected (Session.Connected).
        public bool Connected { get; init; }

        /// A world is loaded and shared (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// This game is a client (Session.IsClient).
        public bool IsClient { get; init; }

        /// The players connected to the host (Session.Peers.Count).
        public int Peers { get; init; }
    }
}
