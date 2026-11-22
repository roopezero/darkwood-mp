namespace DarkwoodMP.World.Rolls
{
    /// <summary>
    /// The night objects of the player's chunk before WormsSpawner.spawn
    /// (WorldEvents.NightObjectBefore); no chunk when none is counted.
    /// </summary>
    internal readonly struct NightCount
    {
        public NightCount(WorldChunk chunk, int count)
        {
            Chunk = chunk;
            Count = count;
        }

        public WorldChunk Chunk { get; }

        public int Count { get; }
    }
}
