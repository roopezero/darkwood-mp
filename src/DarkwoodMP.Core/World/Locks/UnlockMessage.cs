namespace DarkwoodMP.World.Locks
{
    /// What a game sends when one of its locks opened.
    public enum UnlockMessage : byte
    {
        /// Nothing: it didn't open here, isn't shared, or this is a nested
        /// call.
        Nothing = 0,

        /// DoorUnlock, with the door's GameObject and How as x.
        DoorUnlock,

        /// ItemUnlock, with the lock's GameObject and How as x.
        ItemUnlock,
    }
}
