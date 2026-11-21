namespace DarkwoodMP.World.Dragging
{
    /// A push check's outcome and what the watch remembers next.
    public readonly struct PushStep
    {
        public PushOutcome Outcome { get; init; }

        /// It is away from where it is known to be (Loose.Since >= 0).
        public bool Strayed { get; init; }

        /// Since when, if Strayed.
        public Seconds Since { get; init; }

        /// A client's own player was at it while it was away.
        public bool Touched { get; init; }
    }
}
