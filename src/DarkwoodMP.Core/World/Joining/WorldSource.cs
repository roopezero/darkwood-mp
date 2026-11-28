namespace DarkwoodMP.World.Joining
{
    /// Where the world offered to joiners comes from
    /// (SnapshotRules.Source).
    public enum WorldSource
    {
        /// The last save, with what changed since unknown.
        LastSave = 0,

        /// A snapshot saved now (LiveWorld).
        Live,

        /// The save at the epilogue's start: the story as it was then is
        /// kept, and what changed since is sent to the joiner once it is
        /// in (FlagsSync.JoinerInWorld).
        EpilogueSave,
    }
}
