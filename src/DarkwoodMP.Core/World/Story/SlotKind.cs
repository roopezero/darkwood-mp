namespace DarkwoodMP.World.Story
{
    /// Where a prologue stage's place stands in the far-off grid of
    /// OutsideLocations.locationPositions.
    public enum SlotKind : byte
    {
        /// No slot is near it: nothing to change.
        None = 0,

        /// In the last slot taken: a joiner makes it there too.
        Last,

        /// In another slot: a warning, nothing changed.
        Other,
    }
}
