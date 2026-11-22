namespace DarkwoodMP.World.Places
{
    /// A client's session, as the wait for a location's spot sees it.
    public readonly struct WaitFacts
    {
        /// There is a session and this game is a client.
        public bool IsClient { get; init; }

        /// Session.Connected.
        public bool Connected { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }
    }
}
