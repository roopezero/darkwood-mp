namespace DarkwoodMP.World.Inventories
{
    /// What a client undoes when the host refuses one of its slot changes
    /// (SlotShare.OnRefused).
    public enum RefusalUndo : byte
    {
        /// The change is in no batch we remember: undo it alone.
        Alone = 0,

        /// The first refusal of a batch we sent: undo the whole batch,
        /// netted per item type, and mark it undone.
        Batch,

        /// A batch undone already: only the host's slot is set.
        None,
    }
}
