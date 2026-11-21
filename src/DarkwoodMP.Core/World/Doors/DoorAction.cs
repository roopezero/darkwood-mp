namespace DarkwoodMP.World.Doors
{
    /// What a client did to a door: the plugin's WorldActions.Action
    /// values below WindowBarricade, with the same numbers. The host's
    /// check treats any other value below WindowBarricade (0, say) as a
    /// door action with no state check of its own.
    public enum DoorAction : byte
    {
        Open = 1,
        Close = 2,
        Barricade = 3,
        DestroyBarricade = 4,
        Destroy = 5,
        Hit = 6,
        Lock = 7,
        Unlock = 8,
        Undestroy = 9,
        Unblock = 10,
    }
}
