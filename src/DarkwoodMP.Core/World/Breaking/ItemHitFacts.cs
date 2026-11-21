namespace DarkwoodMP.World.Breaking
{
    /// What a game knows after its outermost Item.getHit while sharing.
    public readonly struct ItemHitFacts
    {
        /// The item before the hit.
        public ItemState Before { get; init; }

        /// The item after it.
        public ItemState After { get; init; }

        /// It was our player's hit (BreakRules.OwnerOf, the stack asked).
        public bool Ours { get; init; }

        /// Item.destructible.
        public bool Destructible { get; init; }

        /// Item.destroyToAccessInventory.
        public bool OpensByBreaking { get; init; }

        /// Its GameObject's name.
        public string Name { get; init; }
    }
}
