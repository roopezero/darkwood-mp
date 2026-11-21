namespace DarkwoodMP.World.Dreaming
{
    /// A shared dream's end, as DreamEnd carries it.
    public readonly struct DreamEnded
    {
        /// The outcome the dream ended with (Dreams.outcome).
        public string Outcome { get; init; }

        /// The shared dream that ended (DreamSync.Shared).
        public string Dream { get; init; }
    }
}
