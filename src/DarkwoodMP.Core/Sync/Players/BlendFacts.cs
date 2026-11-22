namespace DarkwoodMP.Sync.Players
{
    /// The two newest snapshots' arrival times, for where a puppet is drawn
    /// between them (RemotePlayer.Tick). All in scaled Time.time, which
    /// Push stamps them with.
    public readonly struct BlendFacts
    {
        /// A previous snapshot is kept (_hasPrev): none after a jump.
        public bool HasPrevious { get; init; }

        public Seconds Previous { get; init; }

        public Seconds Last { get; init; }

        public Seconds Now { get; init; }
    }
}
