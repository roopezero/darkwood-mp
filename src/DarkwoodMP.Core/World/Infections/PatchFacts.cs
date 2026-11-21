namespace DarkwoodMP.World.Infections
{
    /// What a game knows about one patch of an InfectionCleared it took.
    public readonly struct PatchFacts
    {
        /// The patch was found: by its ObjRef, or as the nearest one not
        /// fading within InfectionRules.FindRadius (InfectionSync.Find).
        public bool Found { get; init; }

        /// It is fading already (Infection.disappearing).
        public bool Fading { get; init; }

        /// This game is the host, so the sender's reach counts.
        public bool Host { get; init; }

        /// The sender's stand-in and the patch, for Authority.Near. The rule
        /// sets the reach.
        public ReachFacts Reach { get; init; }
    }
}
