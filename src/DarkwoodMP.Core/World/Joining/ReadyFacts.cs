namespace DarkwoodMP.World.Joining
{
    /// What the host looks at to tell whether a joiner must wait
    /// (WorldShare.HostNotReady).
    public readonly struct ReadyFacts
    {
        /// WorldShare.InWorld and WorldReady.
        public bool InWorld { get; init; }

        public bool WorldReady { get; init; }

        /// The map test's hold is on (JoinGateRules.Hold).
        public bool Holding { get; init; }

        /// Prologue.Film, Prologue.Active, and whether
        /// Prologue.WhyWait(WorldReady) gave a reason.
        public bool Film { get; init; }

        public bool Prologue { get; init; }

        public bool PrologueWaits { get; init; }

        /// Dreams.dreaming, OutsideLocations.playerInOutsideLocation.
        public bool Dreaming { get; init; }

        public bool Away { get; init; }
    }
}
