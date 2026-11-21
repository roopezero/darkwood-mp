namespace DarkwoodMP.World.Objects
{
    /// What the host knows about a vine a client's player cut at (VineHit).
    public readonly struct VineFacts
    {
        /// It is in the host's world (ObjRef.Resolve<Transform>).
        public bool Exists { get; init; }

        /// It has a ChainParent.
        public bool IsVine { get; init; }

        /// ChainParent.attached.
        public bool Attached { get; init; }

        /// ChainParent.health, in the host's world.
        public float Health { get; init; }

        /// The hit's damage (the action's x).
        public float Damage { get; init; }

        /// The requester and the vine's root. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
