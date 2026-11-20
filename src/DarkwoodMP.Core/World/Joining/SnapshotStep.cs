namespace DarkwoodMP.World.Joining
{
    /// Whether the host makes a snapshot for a joiner.
    public enum SnapshotStep
    {
        /// Save one now.
        Make = 0,

        /// None: the last save is offered.
        Skip,

        /// The last one is recent enough: offer it again.
        Reuse,
    }
}
