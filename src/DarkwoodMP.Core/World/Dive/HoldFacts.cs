namespace DarkwoodMP.World.Dive
{
    /// Where the local player holds a full oxygen tank, as the dive's
    /// requirement looks.
    public readonly struct HoldFacts
    {
        /// In the bags (Inventory.getItemInPlayer, the additional
        /// inventory too).
        public bool InBags { get; init; }

        /// Among the journal's items (UI.journal.itemsDict).
        public bool InJournal { get; init; }

        /// The game's Flags are there to set the flag in.
        public bool Flags { get; init; }
    }
}
