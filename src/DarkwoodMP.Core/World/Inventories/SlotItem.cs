namespace DarkwoodMP.World.Inventories
{
    /// What the slot rules look at in the item in a slot (InvItemClass, and
    /// its baseClass, an InvItem). The zero value is an empty slot.
    public readonly struct SlotItem
    {
        /// The slot holds an item (not InvItemClass.isNull).
        public bool Filled { get; init; }

        /// InvItemClass.type.
        public string Type { get; init; }

        /// InvItemClass.amount.
        public int Amount { get; init; }

        /// InvItemClass.durability.
        public float Durability { get; init; }

        /// InvItemClass.isRecipe.
        public bool Recipe { get; init; }

        /// How many upgrades it has (InvItemClass.upgrades.Count; 0 when
        /// the list is null).
        public int Upgrades { get; init; }

        /// It has a baseClass.
        public bool HasBase { get; init; }

        /// It rots (baseClass.rottenItem isn't null).
        public bool Rots { get; init; }

        /// baseClass.regeneratesWhenInactive: it recharges while off.
        public bool Regenerates { get; init; }

        /// baseClass.durabilityDrain: how fast it drains while on.
        public float Drain { get; init; }

        /// baseClass.maxDurability.
        public float MaxDurability { get; init; }
    }
}
