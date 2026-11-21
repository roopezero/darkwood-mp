namespace DarkwoodMP.World.Dragging
{
    /// What our player letting go does to the owner table (DragSync.LetGo).
    public enum LetGoEntry : byte
    {
        /// Not our entry: nothing.
        Keep = 0,

        /// Client: our entry is removed. The host frees it by our end.
        Forget,

        /// Host: our entry is removed and DragOwner(0, stay) goes to
        /// everyone.
        Tell,
    }
}
