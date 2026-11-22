namespace DarkwoodMP.World.Joining
{
    /// The WorldReady latch after one read.
    public readonly struct ReadyStep
    {
        public bool Ready { get; init; }

        /// When the world first looked ready; -1 when it doesn't.
        public Seconds Since { get; init; }
    }
}
