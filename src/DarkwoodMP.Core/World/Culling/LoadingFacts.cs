namespace DarkwoodMP.World.Culling
{
    /// The host, every frame, while its player may be being taken to a
    /// place (SharedCullingPatch.RefreshWhileLoading).
    public readonly struct LoadingFacts
    {
        /// Proxies.Any.
        public bool StandIns { get; init; }

        /// OutsideLocations exists and is loading.
        public bool Loading { get; init; }

        /// WorldGrid exists and its disableRefresh is false.
        public bool Refreshes { get; init; }

        /// Player.Instance isn't null.
        public bool Player { get; init; }

        /// Core.loadingGame.
        public bool LoadingGame { get; init; }

        /// Core.worldGenFinished().
        public bool WorldMade { get; init; }

        /// Time.realtimeSinceStartup.
        public Seconds Now { get; init; }

        /// When the next refresh may run (_loadingRefreshAt).
        public Seconds Next { get; init; }
    }
}
