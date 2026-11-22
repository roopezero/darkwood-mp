namespace DarkwoodMP.World.Teardown
{
    /// When pathfinding's pending updates are finished before a scan
    /// (ScanWait.BeforeScan). WorldTeardown's own choices are a flag set by
    /// three prefixes and the game quitting, and cleared at a Single scene
    /// load: no rule to copy.
    public static class TeardownRules
    {
        /// The pending graph updates are finished before a scan: playing,
        /// with graphs, not scanning already, not inside a work item, and
        /// with something queued.
        public static bool Flushes(in ScanFacts facts) =>
            facts.Playing
                && facts.Graphs
                && !facts.Scanning
                && !facts.InWorkItem
                && (facts.WorkQueued || facts.UpdatesQueued);
    }
}
