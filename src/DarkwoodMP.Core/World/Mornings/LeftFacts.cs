namespace DarkwoodMP.World.Mornings
{
    /// The host, when a joiner says they walked out (AfterNightLeft).
    public readonly struct LeftFacts
    {
        /// There is a session and this game is its host.
        public bool IsHost { get; init; }

        /// The sender is one of Session.Peers.
        public bool FromPeer { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Singleton<Controller>.Instance exists.
        public bool HasController { get; init; }

        /// The host is in a morning (Controller.isAfterNight).
        public bool InMorning { get; init; }

        /// The host's player dreams (Dreams.dreaming).
        public bool Dreaming { get; init; }

        /// The host talks to its morning visitor (MorningRules.Talking).
        public bool Talking { get; init; }
    }
}
