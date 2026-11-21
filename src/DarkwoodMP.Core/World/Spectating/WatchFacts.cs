namespace DarkwoodMP.World.Spectating
{
    /// One frame of watching (Spectator.Run).
    public readonly struct WatchFacts
    {
        /// Watching in the epilogue (Spectator.Epilogue).
        public bool Epilogue { get; init; }

        /// A session is set, Active and Connected, and WorldShare.InWorld.
        public bool InSession { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// How many players there are to watch (Roster.Playing); not
        /// looked at out of the session.
        public int Playing { get; init; }

        /// Nobody was there to watch on the frames before (nobodySince is
        /// set).
        public bool Nobody { get; init; }

        /// Since when, in unscaled time.
        public Seconds NobodySince { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }
    }
}
