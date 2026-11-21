using System;

namespace DarkwoodMP.World.Doors
{
    /// What a client does to its door to match the host's state, in the
    /// order of the values (the plugin's OnObjState, Kind.Door).
    [Flags]
    public enum DoorFixes
    {
        None = 0,

        /// Door.destroyDoor(true): silently, a rollback.
        Break = 1,

        /// Door.unDestroy.
        Mend = 2,

        /// Door.lockMe with the host's key type.
        Lock = 4,

        /// Door.unlock.
        Unlock = 8,

        /// barricadeState = 2, then Door.barricade(false).
        Barricade = 16,

        /// Door.destroyBarricade(false).
        RemoveBarricade = 32,

        /// Door.open from position + right: always the same side.
        Open = 64,

        /// Door.close.
        Close = 128,
    }
}
