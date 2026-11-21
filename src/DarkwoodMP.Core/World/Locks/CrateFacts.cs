namespace DarkwoodMP.World.Locks
{
    /// What the host knows about an Item without a Trigger that a client
    /// says its player picked (a TrapDisarm on it).
    public readonly struct CrateFacts
    {
        /// It is a crate picked open with the progress bar
        /// (LockRules.IsCrate).
        public bool Crate { get; init; }

        /// The requester and the item. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
