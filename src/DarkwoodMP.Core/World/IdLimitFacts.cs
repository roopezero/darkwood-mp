namespace DarkwoodMP.World
{
    /// What the limits on shared save ids look at (SharedIdRules).
    public readonly struct IdLimitFacts
    {
        /// This game hosts the session. False with no session.
        public bool Host { get; init; }

        /// Host: the counters of the joiners let in to this world.
        public JoinerIds Joiners { get; init; }

        /// The save id counter of the save this game last loaded.
        public int Loaded { get; init; }
    }
}
