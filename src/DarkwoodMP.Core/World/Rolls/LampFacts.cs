namespace DarkwoodMP.World.Rolls
{
    /// One of a generator's power items, as the shadows' lights pick a lamp
    /// (ShadowRules.Choices).
    public readonly struct LampFacts
    {
        /// It is the generator's own Item (Generator.GetComponent<Item>).
        public bool Own { get; init; }

        /// It is gone (null, or destroyed: Unity's null check).
        public bool Missing { get; init; }

        /// The shadows never turn it on (Item.dontTurnOnByShadowsEvent).
        public bool KeptDark { get; init; }
    }
}
