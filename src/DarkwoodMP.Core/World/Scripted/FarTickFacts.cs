namespace DarkwoodMP.World.Scripted
{
    /// Host, each culling refresh with players (FarEvents.Tick).
    public readonly struct FarTickFacts
    {
        /// A session exists and this game is its host.
        public bool Host { get; init; }

        /// WorldShare.InWorld and Core.loadingGame.
        public bool InWorld { get; init; }

        public bool Loading { get; init; }

        /// Dreams.dreaming or wantToDream.
        public bool Dreaming { get; init; }

        /// Whether the list of world locations was made, and when (real
        /// time since the start).
        public bool Listed { get; init; }

        public Seconds ListedAt { get; init; }

        /// Real time since the start (Time.realtimeSinceStartup).
        public Seconds Now { get; init; }
    }
}
