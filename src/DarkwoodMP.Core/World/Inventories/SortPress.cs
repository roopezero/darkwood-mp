namespace DarkwoodMP.World.Inventories
{
    /// What pressing Sort & Stack does (SlotShare.SortPressed).
    public enum SortPress : byte
    {
        /// Nothing: an item on the cursor still belongs to a slot.
        Nothing = 0,

        /// A client's storage with no name in the host's world: a warning.
        NoName,

        /// A client still waits for the host's contents: logged, no sort.
        StillWaiting,

        /// A client sorts its copy as a prediction and asks the host to
        /// sort (StorageSort in a request).
        PredictAndAsk,

        /// The host, or a game outside a world, sorts at once.
        Sort,
    }
}
