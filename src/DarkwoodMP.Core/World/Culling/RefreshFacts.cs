namespace DarkwoodMP.World.Culling
{
    /// A call of WorldGrid.refreshPosition (SharedCulling.Prefix).
    public readonly struct RefreshFacts
    {
        /// currentGrid isn't null.
        public bool Grid { get; init; }

        /// Proxies.Any: there are remote players' stand-ins.
        public bool StandIns { get; init; }

        /// The refresh's instant argument.
        public bool Instant { get; init; }

        /// Session.Instance is set and Active.
        public bool Session { get; init; }
    }
}
