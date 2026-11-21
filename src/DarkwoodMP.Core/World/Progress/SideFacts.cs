namespace DarkwoodMP.World.Progress
{
    /// Which side of the shared progression our game is on (Progression's
    /// Solo, HostSide and ClientActive).
    public readonly struct SideFacts
    {
        /// A session is there and active (ModHost.Session.Active).
        public bool SessionActive { get; init; }

        /// A session is there and we host it.
        public bool IsHost { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// NetEnemies.ClientMode: we play in the host's world.
        public bool ClientMode { get; init; }

        /// Client: the host has sent the shared state (Progression._known).
        public bool Known { get; init; }

        /// Hideouts.Together: everyone shares one hideout.
        public bool Together { get; init; }

        /// The host's shared record is there (Progression._saved).
        public bool HasRecord { get; init; }

        /// The host knows which world's file the record is for
        /// (Progression._savedPath isn't null).
        public bool HasPath { get; init; }

        /// Player.Instance is there.
        public bool HasPlayer { get; init; }
    }
}
