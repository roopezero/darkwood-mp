namespace DarkwoodMP.World.Inventories
{
    /// What a client does with the host's contents of one slot.
    public enum SlotStep : byte
    {
        /// Nothing: the slot holds what the host's does.
        None = 0,

        /// Take the host's contents (SlotShare.Set).
        Take,

        /// Keep ours: we changed it from what the host has too, and the
        /// change goes at the next look.
        Keep,

        /// Take the host's contents and undo our change, as the host would
        /// refuse it.
        TakeAndUndo,
    }
}
