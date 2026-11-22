namespace DarkwoodMP.Menus
{
    /// The session as the Direct section sees it (DirectPanel.Draw).
    public readonly struct DirectFacts
    {
        /// A session is active (Session.Active).
        public bool Active { get; init; }

        /// The session runs on the TCP backend (Session.Backend is the
        /// panel's TcpBackend), not on Steam.
        public bool OnTcp { get; init; }

        /// The player hosts the session (Session.IsHost).
        public bool Host { get; init; }

        /// The TCP backend is hosting, connecting or connected
        /// (TcpBackend.InSession), whether or not a session is active yet.
        public bool TcpBusy { get; init; }
    }
}
