namespace DarkwoodMP.World.Spectating
{
    /// A joiner dead for good waits for someone to watch
    /// (Spectator.WaitAndBegin), one round of it.
    public readonly struct DeadWaitFacts
    {
        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// When the wait gives up.
        public Seconds Until { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Spectator.Active: watching already.
        public bool Active { get; init; }

        /// How many players there are to watch (Roster.Playing).
        public int Playing { get; init; }
    }
}
