namespace DarkwoodMP.World.Owed
{
    /// Whether the host's record of owed parts is written beside the save
    /// as the game saves (OwedRules.Writes).
    public readonly struct RecordSaveFacts
    {
        /// The game saves a snapshot for a joiner (WorldShare.Snapshotting),
        /// not the host's own save.
        public bool Snapshotting { get; init; }

        /// This game plays the host's world (NetEnemies.ClientMode).
        public bool Client { get; init; }

        /// The record was read in this process (OwedParts._data isn't
        /// null).
        public bool Loaded { get; init; }

        /// The profile folder now (OwedParts.ProfileDir: null without a
        /// SaveManager or a profile).
        public string Profile { get; init; }

        /// The profile folder the record was read from (OwedParts._dir).
        public string RecordProfile { get; init; }

        /// The record holds nothing (OwedRecord.Empty), when Loaded.
        public bool Empty { get; init; }
    }
}
