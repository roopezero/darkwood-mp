namespace DarkwoodMP.World.Windows
{
    /// What a client does to its window to match the host's state (the
    /// plugin's Windows.RollBack). The barricade's health is set
    /// to the host's whatever this says.
    public enum WindowFix
    {
        None,

        /// barricadeState = 2, then Window.barricade with the host's
        /// barricade health.
        Barricade,

        /// Window.destroyBarricade(false).
        RemoveBarricade,
    }
}
