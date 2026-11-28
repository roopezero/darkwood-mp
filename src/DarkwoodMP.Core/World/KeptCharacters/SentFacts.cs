namespace DarkwoodMP.World.KeptCharacters
{
    /// What a client's game looks at to choose which state of its player it
    /// sends as its character (Characters.SaveNow).
    public readonly struct SentFacts
    {
        /// Dreams.dreaming: our player is in a dream, with the dream's bags.
        public bool Dreaming { get; init; }

        /// Dreams.inventorySlotsCopy has slots: the game put the real bags
        /// away as the dream began (it doesn't on the first play, whose
        /// prologue dream starts with nothing to keep).
        public bool Copied { get; init; }
    }
}
