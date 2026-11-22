namespace DarkwoodMP.World.Inventories
{
    /// Whether a container is shut in the host's world, as its Item and
    /// Locked components say.
    public readonly struct LockFacts
    {
        /// A crate: its Item has a disarmTime above 0, an Inventory and no
        /// Trigger (WorldActions.Crate).
        public bool Crate { get; init; }

        /// Item.searched: the crate was picked open.
        public bool Searched { get; init; }

        /// It has a Locked component, and Locked.locked is true.
        public bool Locked { get; init; }

        /// Item.destroyToAccessInventory: it has to be broken open first
        /// (a larva egg).
        public bool DestroyToAccess { get; init; }

        /// Item.destroyed.
        public bool Destroyed { get; init; }
    }
}
