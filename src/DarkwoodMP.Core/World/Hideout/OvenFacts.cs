namespace DarkwoodMP.World.Hideout
{
    /// The host, checking a joiner's lit oven (OvenLit).
    public readonly struct OvenFacts
    {
        /// The oven resolved here (ObjRef.Resolve<ExperienceMachine>).
        public bool Exists { get; init; }

        /// The requester's reach to it (Authority.InReach's facts, with
        /// ReachRules.Reach and the oven's name).
        public ReachFacts Reach { get; init; }

        /// Some Location holds it (transform.GetLocation()).
        public bool AtLocation { get; init; }
    }
}
