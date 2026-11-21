namespace DarkwoodMP.World.Dragging
{
    /// What the host does for a joiner with a thing that moved since its
    /// snapshot.
    public enum JoinerPlace : byte
    {
        /// Not sent.
        Skip = 0,

        /// Still moving and watched: known to be at the snapshot's place
        /// and not away, so the watch sends it to everyone once it is at
        /// rest. Counted as later.
        Later,

        /// Sent to the joiner where it is (ObjMove, last), if its pinned
        /// name is valid.
        Send,
    }
}
