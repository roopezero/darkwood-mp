namespace DarkwoodMP.World.Clock
{
    /// What the host looks at each frame to send its clock (TimeSync).
    public readonly struct ClockSendFacts
    {
        /// Session.IsHost.
        public bool IsHost { get; init; }

        /// Session.Peers.Count.
        public int Peers { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// The game's Controller exists.
        public bool HasController { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// When the next TimeSync is due (unscaled). 0 sends at once.
        public Seconds NextSend { get; init; }

        /// Dreams exists and Dreams.dreaming.
        public bool Dreaming { get; init; }

        /// Dreams.timeCopy: the world's minute, kept while dreaming.
        public float TimeCopy { get; init; }

        /// Controller.CurrentTime: the minute of the day the clock shows.
        public int Minute { get; init; }
    }
}
