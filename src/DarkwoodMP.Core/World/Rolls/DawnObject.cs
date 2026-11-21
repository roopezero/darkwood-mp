namespace DarkwoodMP.World.Rolls
{
    /// One of the dawn's random objects, as the host sends it
    /// (RandomObjectsMessage.Dawn).
    public readonly struct DawnObject
    {
        /// Its spawned id (ObjRef).
        public long Id { get; init; }

        /// Its chunk's index in WorldGenerator.worldChunks (u16).
        public int Chunk { get; init; }

        /// Its index in RandomWorldObjects.objects (u16).
        public int Object { get; init; }

        /// Its position in the chunk.
        public Vec3 Local { get; init; }

        /// What it holds (Inventory.SaveState JSON, "" none).
        public string Contents { get; init; }
    }
}
