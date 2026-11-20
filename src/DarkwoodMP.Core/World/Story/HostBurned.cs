namespace DarkwoodMP.World.Story
{
    /// What a joiner heard of the host's EpilogueOutcomes.burnedForest
    /// (EpilogueSync._hostBurned: -1, 0 or 1).
    public enum HostBurned : byte
    {
        /// Nothing heard (-1).
        Unheard = 0,

        /// Not burned (0).
        No,

        /// Burned (1).
        Yes,
    }
}
