namespace DarkwoodMP.World.Dragging
{
    /// What a DragOwner does to the owner table.
    public enum OwnerEntry : byte
    {
        /// Not taken at all.
        Ignore = 0,

        /// The entry is removed (owner 0).
        Forget,

        /// The entry is set to the owner, heard now.
        Remember,
    }
}
