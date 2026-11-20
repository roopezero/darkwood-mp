namespace DarkwoodMP.World.Workbenches
{
    /// What WorkbenchRules.Update decided: the state to keep, and whether
    /// (and why) the workbenches are given their rows now.
    public readonly struct RowsUpdate
    {
        public RowsApplied Applied { get; init; }

        /// RowsWhy.None: nothing to do this frame.
        public RowsWhy Why { get; init; }
    }
}
