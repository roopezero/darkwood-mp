namespace DarkwoodMP.World.Scripted
{
    /// What the host does with its record of whether its player is in a
    /// location (FarEvents.HostIn).
    public enum FarRecord : byte
    {
        /// Leave it.
        Keep = 0,

        /// Store HostIn.
        Store,

        /// Remove it.
        Forget,
    }
}
