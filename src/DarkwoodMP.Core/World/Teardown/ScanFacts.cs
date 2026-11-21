namespace DarkwoodMP.World.Teardown
{
    /// What AstarPath shows before a scan.
    public readonly struct ScanFacts
    {
        /// Application.isPlaying, and AstarPath's graphs not null.
        public bool Playing { get; init; }

        public bool Graphs { get; init; }

        /// isScanning, and the private processingWorkItems.
        public bool Scanning { get; init; }

        public bool InWorkItem { get; init; }

        /// The private workItemsQueued, and IsAnyGraphUpdatesQueued.
        public bool WorkQueued { get; init; }

        public bool UpdatesQueued { get; init; }
    }
}
