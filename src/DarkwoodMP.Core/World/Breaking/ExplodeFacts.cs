namespace DarkwoodMP.World.Breaking
{
    /// What the host knows about an exploding barrel or gas bottle when a
    /// client says it went off (Explode).
    public readonly struct ExplodeFacts
    {
        /// It is in the host's world (ObjRef.Resolve<Explodes>).
        public bool Exists { get; init; }

        /// It has a Character: an enemy's, which the host's enemies own.
        public bool OnCharacter { get; init; }

        /// It went off in the host's world (BreakSync.Activated).
        public bool WentOff { get; init; }

        /// The requester and the explosive. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
