namespace DarkwoodMP.World.Traders
{
    /// What comes of the hideout's spawnTrader for a joiner's morning
    /// trader (TraderCopyRules.AfterSpawn).
    public enum SpawnResult
    {
        /// The hideout spawned no trader: no copy.
        NoTrader,

        /// The one asked about: kept as a copy.
        Kept,

        /// Another trader, who was there before: left, no copy.
        OtherStays,

        /// Another trader, spawned just now: taken away again
        /// (Location.despawnTrader), no copy.
        OtherGoes,
    }
}
