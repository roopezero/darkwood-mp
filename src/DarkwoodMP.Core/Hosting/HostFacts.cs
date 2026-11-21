namespace DarkwoodMP.Hosting
{
    /// What decides whether the player may host the world they're in.
    public readonly struct HostFacts
    {
        /// A world is loaded and being played (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// The world is the player's copy of another host's world
        /// (WorldShare.Instance.PlayingHostCopy; false without a WorldShare).
        public bool HostsCopy { get; init; }

        /// The world is in the epilogue, with nobody else in it
        /// (EpilogueSync.InEpilogueAlone()).
        public bool InEpilogue { get; init; }
    }
}
