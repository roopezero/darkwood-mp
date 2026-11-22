namespace DarkwoodMP.World.Clock
{
    /// What a client looks at when the host's clock comes (TimeSync).
    public readonly struct ClockFacts
    {
        /// The game's Controller exists.
        public bool HasController { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Dreams exists, and Dreams.dreaming or Dreams.switchingDream.
        public bool Dreaming { get; init; }

        /// The host's day and minute of the day, from the message.
        public int HostDay { get; init; }

        public int HostMinute { get; init; }

        /// Our Controller.day and Controller.CurrentTime.
        public int Day { get; init; }

        public int Minute { get; init; }
    }
}
