namespace DarkwoodMP.World.Windows
{
    /// What a client did to a window: the plugin's WorldActions.Action
    /// values from WindowBarricade to below TrapFired, with the same
    /// numbers. The host's check treats any other value in that range
    /// (14 to 20) as a window action with no state check of its own.
    public enum WindowAction : byte
    {
        Barricade = 11,
        DestroyBarricade = 12,
        Hit = 13,
    }
}
