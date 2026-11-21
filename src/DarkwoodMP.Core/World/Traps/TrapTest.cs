namespace DarkwoodMP.World.Traps
{
    /// The map test's trap-refuse (the plugin's TrapSync.TestRefuse):
    /// how the host makes the next TrapFired request fail. Null there is
    /// None, "race" is Race, and any other text is Gone.
    public enum TrapTest : byte
    {
        None = 0,

        /// The trap went off in the host's game just before.
        Race,

        /// The trap was taken out of the host's game just before.
        Gone,
    }
}
