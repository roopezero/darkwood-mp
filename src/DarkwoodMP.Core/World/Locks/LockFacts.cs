namespace DarkwoodMP.World.Locks
{
    /// What the host knows about a lock on something that isn't a door (a
    /// chest, a cabinet) when a client says it opened it (ItemUnlock).
    public readonly struct LockFacts
    {
        /// The lock is in the host's world (ObjRef.Resolve<Locked>).
        public bool Exists { get; init; }

        /// Locked.locked.
        public bool Locked { get; init; }

        /// How the client opened it: the action's x, which LockRules names
        /// (ByStory, ByKey, ByLockpick). Any other value passes unchecked.
        public float How { get; init; }

        /// The key is in the host's journal (WorldActions.HasKey: the
        /// journal's keysDict holds a Locked.keyType that isn't empty).
        public bool KeyKnown { get; init; }

        /// Locked.keyType, for the refusal's words.
        public string KeyType { get; init; }

        /// The requester and the lock. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
