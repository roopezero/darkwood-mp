namespace DarkwoodMP.World.Owed
{
    /// Whether the host's record of owed parts is read from beside the
    /// save before it is used (OwedRules.Reads).
    public readonly struct RecordReadFacts
    {
        /// The record was read in this process (OwedParts._data isn't
        /// null).
        public bool Loaded { get; init; }

        /// The profile folder now (OwedParts.ProfileDir).
        public string Profile { get; init; }

        /// The profile folder the record was read from (OwedParts._dir).
        public string RecordProfile { get; init; }

        /// The active scene's handle.
        public int Scene { get; init; }

        /// The scene handle the record's HostOwed is of
        /// (OwedParts._owedScene).
        public int OwedScene { get; init; }
    }
}
