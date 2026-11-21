namespace DarkwoodMP.World.Hideout
{
    /// What the porter moved, as PorterDeliveredMessage carries it.
    public readonly struct PorterDelivery
    {
        /// The hideout id it took things from.
        public int Source { get; init; }

        /// The hideout id it delivered to.
        public int Target { get; init; }

        /// The package's id (ObjRef.Spawned).
        public long Id { get; init; }

        /// The package spawner's id (ObjRef.Spawned).
        public long SpawnerId { get; init; }

        /// What's in the package (Inventory.SaveState as JSON).
        public string Contents { get; init; }
    }
}
