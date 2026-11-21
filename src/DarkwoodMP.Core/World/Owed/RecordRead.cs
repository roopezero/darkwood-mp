namespace DarkwoodMP.World.Owed
{
    /// What of the host's record is read from the file (OwedRules.Reads).
    public enum RecordRead : byte
    {
        /// Nothing: the one in memory is this world's.
        None = 0,

        /// All of it, the first time or for another profile; the record's
        /// profile and scene become the current ones.
        Whole,

        /// Only HostOwed, at a new world: the host's player is where the
        /// save has them, and so is what they are owed. What the joiners
        /// had stays, as their characters do.
        HostOwed,
    }
}
