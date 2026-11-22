namespace DarkwoodMP.World.Inventories
{
    /// What the host looks at when a client presses Sort & Stack
    /// (SlotShare.OnSort).
    public readonly struct SortFacts
    {
        /// The storage is in the host's world, and is a workbench's
        /// storage (SlotShare.Is).
        public bool StorageHere { get; init; }

        /// ContainerRules.MayChange for the client and the storage.
        public Verdict Container { get; init; }

        /// The host's player holds an item on the cursor that came from
        /// this storage.
        public bool HostMoving { get; init; }
    }
}
