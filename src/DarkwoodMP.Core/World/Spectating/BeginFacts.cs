namespace DarkwoodMP.World.Spectating
{
    /// The game is about to show the permadeath screen
    /// (Spectator.TryBegin).
    public readonly struct BeginFacts
    {
        /// Spectator.AllowPermadeath: nobody was left to watch.
        public bool AllowPermadeath { get; init; }

        /// A session is set, Active and Connected.
        public bool InSession { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// How many players there are to watch (Roster.Playing).
        public int Playing { get; init; }
    }
}
