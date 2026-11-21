namespace DarkwoodMP.World.Locks
{
    /// What the host knows about a container when a client says its player
    /// opened it (ItemSearched).
    public readonly struct SearchFacts
    {
        /// The container is in the host's world (ObjRef.Resolve<Item>).
        public bool Exists { get; init; }

        /// It is a crate picked open with the progress bar
        /// (LockRules.IsCrate).
        public bool Crate { get; init; }

        /// Item.searched.
        public bool Searched { get; init; }

        /// It has an Inventory.
        public bool HasInventory { get; init; }

        /// The requester and the container. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
