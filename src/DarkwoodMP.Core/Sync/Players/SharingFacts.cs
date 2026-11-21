namespace DarkwoodMP.Sync.Players
{
    /// The session and the world, for whether our player's sounds, shots
    /// and wounds are shared (PlayerFx.Sharing).
    public readonly struct SharingFacts
    {
        /// Session.Active.
        public bool Active { get; init; }

        /// Session.Connected.
        public bool Connected { get; init; }

        /// Session.IsClient.
        public bool Client { get; init; }

        /// Session.Peers.Count.
        public int Peers { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }
    }
}
