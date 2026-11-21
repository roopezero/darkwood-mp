namespace DarkwoodMP.World.Breaking
{
    /// What the host knows about a destructible Item (furniture, a crate)
    /// when a client says it hit, broke or repaired it.
    public readonly struct BreakFacts
    {
        /// It is in the host's world (ObjRef.Resolve<Item>).
        public bool Exists { get; init; }

        /// Item.destroyed: it is broken.
        public bool Destroyed { get; init; }

        /// Item.destructible.
        public bool Destructible { get; init; }

        /// A hit's damage (the action's x).
        public float Damage { get; init; }

        /// The requester and the item. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
