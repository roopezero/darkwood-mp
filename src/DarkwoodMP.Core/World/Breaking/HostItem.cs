namespace DarkwoodMP.World.Breaking
{
    /// An Item as the host has it, sent in an ObjState so a client can put
    /// its copy back after a refused request (ItemObjState).
    public readonly struct HostItem
    {
        /// Item.isOn: switched on.
        public bool On { get; init; }

        /// Its Locked is locked.
        public bool Locked { get; init; }

        /// Item.searched.
        public bool Searched { get; init; }

        /// Item.destroyed: broken.
        public bool Broken { get; init; }

        /// Its InteractiveItem is on.
        public bool Switched { get; init; }

        /// Its Feeder is full (Feeder.Active).
        public bool FeederFull { get; init; }

        /// Item.health.
        public int Health { get; init; }
    }
}
