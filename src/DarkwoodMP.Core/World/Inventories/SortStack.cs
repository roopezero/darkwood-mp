namespace DarkwoodMP.World.Inventories
{
    /// What Sort & Stack looks at in one filled slot of a storage: the
    /// item's saved state (InvItemClass.SaveState) and its baseClass (an
    /// InvItem).
    public readonly struct SortStack
    {
        /// The slot it's in (SaveState.slotId).
        public int Slot { get; init; }

        public string Type { get; init; }

        public int Amount { get; init; }

        public float Durability { get; init; }

        /// SaveState.isRecipe.
        public bool Recipe { get; init; }

        /// SaveState.recipeFor: what a recipe makes.
        public string RecipeFor { get; init; }

        /// How many upgrades it has (0 when the list is null).
        public int Upgrades { get; init; }

        /// How many modifiers it has (0 when the list is null).
        public int Modifiers { get; init; }

        /// It has a baseClass. Without one it's only moved.
        public bool HasBase { get; init; }

        /// baseClass.stackable.
        public bool Stackable { get; init; }

        /// baseClass.hasAmmo: it holds rounds.
        public bool HasAmmo { get; init; }

        /// baseClass.maxAmount: the most one stack holds.
        public int MaxAmount { get; init; }

        /// baseClass.stacksDurability: its fuel is its durability
        /// (gasoline, the lantern).
        public bool StacksDurability { get; init; }

        /// baseClass.maxDurability.
        public float MaxDurability { get; init; }

        /// baseClass.canBeReloaded.
        public bool Reloadable { get; init; }

        /// baseClass.repairable.
        public bool Repairable { get; init; }

        /// baseClass.regeneratesWhenInactive.
        public bool Regenerates { get; init; }

        /// It rots (baseClass.rottenItem isn't null).
        public bool Rots { get; init; }

        /// The number of baseClass.categories[0], or SortRules.NoCategory
        /// when it has none (or no baseClass).
        public int Category { get; init; }
    }
}
