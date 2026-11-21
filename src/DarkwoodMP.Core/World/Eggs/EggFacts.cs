namespace DarkwoodMP.World.Eggs
{
    /// What the host knows about the egg a client says it broke.
    public readonly struct EggFacts
    {
        /// The egg was found: by its ObjRef, or by its name near where the
        /// client saw it (EggSync.Find).
        public bool Found { get; init; }

        /// Item.destructible.
        public bool Destructible { get; init; }

        /// Item.destroyToAccessInventory: it is broken open to get at its
        /// inventory.
        public bool OpensByBreaking { get; init; }

        /// Item.destroyed in the host's world.
        public bool Destroyed { get; init; }

        /// Its GameObject's name.
        public string Name { get; init; }

        /// The requester and the egg. The rule sets the reach and the name.
        public ReachFacts Reach { get; init; }
    }
}
