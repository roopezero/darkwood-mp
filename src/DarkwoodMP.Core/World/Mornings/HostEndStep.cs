namespace DarkwoodMP.World.Mornings
{
    /// What the host does when its own morning ended. Zero: nothing.
    public readonly struct HostEndStep
    {
        /// Remember today's morning as over (LateMornings.Over).
        public bool MarkOver { get; init; }

        /// Send AfterNightEnded to everyone.
        public bool Send { get; init; }
    }
}
