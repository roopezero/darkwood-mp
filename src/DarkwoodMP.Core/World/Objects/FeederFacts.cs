namespace DarkwoodMP.World.Objects
{
    /// What the host knows about a feeder a client's player used
    /// (FeederUsed).
    public readonly struct FeederFacts
    {
        /// It is in the host's world (ObjRef.Resolve<Transform>).
        public bool Exists { get; init; }

        /// It has a Feeder.
        public bool IsFeeder { get; init; }

        /// Feeder.Active: it hasn't been used today.
        public bool Active { get; init; }

        /// The requester and the feeder. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
