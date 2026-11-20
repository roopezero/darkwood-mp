namespace DarkwoodMP.World.Inventories
{
    /// How the mod shares a container the local player has open.
    public enum ContainerPath : byte
    {
        /// Not shared: not a world container, or one only our player has.
        Ignore = 0,

        /// Slot by slot (SlotShare), with no lock: a workbench's storage.
        Slots,

        /// Slot by slot, one player at a time: a client's other containers.
        LockedSlots,

        /// The whole contents, one player at a time: the host's containers.
        Whole,
    }
}
