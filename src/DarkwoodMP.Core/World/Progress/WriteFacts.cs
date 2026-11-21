namespace DarkwoodMP.World.Progress
{
    /// What decides whether the game's save writes the shared record
    /// beside it (Progression.OnHostSaved).
    public readonly struct WriteFacts
    {
        /// WorldShare.Snapshotting: the save is a copy for a joining player.
        public bool Snapshotting { get; init; }

        /// NetEnemies.ClientMode.
        public bool ClientMode { get; init; }

        /// The record is there (Progression._saved).
        public bool HasRecord { get; init; }

        /// The record's path is known (Progression._savedPath).
        public bool HasPath { get; init; }

        /// The record's path is the current world's (SavedPath()).
        public bool PathCurrent { get; init; }
    }
}
