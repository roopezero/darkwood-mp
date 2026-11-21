namespace DarkwoodMP.World.Story
{
    /// Player.die has run (EpilogueSync.AfterDie).
    public readonly struct DeathFacts
    {
        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// Our death was counted already (EpilogueSync._ownDeath).
        public bool Counted { get; init; }

        /// A session is set and Active.
        public bool InSession { get; init; }

        /// The player that died is there.
        public bool HasPlayer { get; init; }

        /// Its inEpilogue.
        public bool InEpilogue { get; init; }

        /// Its crawling.
        public bool Crawling { get; init; }

        /// Its dying.
        public bool Dying { get; init; }
    }
}
