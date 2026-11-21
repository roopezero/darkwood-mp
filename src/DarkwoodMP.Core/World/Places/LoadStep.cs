namespace DarkwoodMP.World.Places
{
    /// The host, while a location loads for the joiners.
    public enum LoadStep : byte
    {
        /// Not loaded yet: wait.
        Wait = 0,

        /// It is in spawnedLocations: scan its graph, count it, done.
        Loaded,

        /// It didn't load in time: a warning, done.
        GaveUp,
    }
}
