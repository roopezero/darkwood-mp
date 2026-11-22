namespace DarkwoodMP.World.Nights
{
    /// A pending second arming of a client's night object.
    public enum RearmStep : byte
    {
        /// Not yet: keep it.
        Wait = 0,

        /// Set the trap to the host's state, and drop it.
        Set,

        /// The trap is gone or right already: drop it.
        Drop,
    }
}
