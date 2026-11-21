namespace DarkwoodMP.World.Dragging
{
    /// What the once-a-second sweep (DragSync.FreeStale) does with an
    /// entry of the owner table.
    public enum OwnerSweep : byte
    {
        /// Not stale.
        Keep = 0,

        /// Host: freed (Free, "stopped sending").
        Free,

        /// Client: removed, with no message.
        Forget,
    }
}
