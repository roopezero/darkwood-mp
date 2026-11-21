namespace DarkwoodMP.World.Progress
{
    /// When the host's game keeps the shared record, starts it and writes
    /// it (Progression.HostSaved, OnHostSaved). The file itself stays in
    /// the shell.
    public static class RecordRules
    {
        /// Whether our game keeps a record at all: playing alone or
        /// hosting, in a world that isn't loading, with a player, not in
        /// the host's world, and a path for the file.
        public static bool Keeps(in RecordFacts record)
        {
            if (!record.Solo && !record.IsHost)
            {
                return false;
            }

            return record.InWorld
                && !record.Loading
                && record.HasPlayer
                && !record.ClientMode
                && record.HasPath;
        }

        /// Whether a record starts from our player's own essence and level:
        /// none read from the file, the caller may start one, and either
        /// alone with our own player's reset or hosting with someone
        /// connected.
        public static bool Starts(in RecordFacts record)
        {
            if (record.HasRecord || !record.Start)
            {
                return false;
            }

            if (record.Solo)
            {
                return record.Alone;
            }

            return record.Peers > 0;
        }

        /// Whether the game's save writes the record beside it.
        public static bool Writes(in WriteFacts write)
        {
            return !write.Snapshotting
                && !write.ClientMode
                && write.HasRecord
                && write.HasPath
                && write.PathCurrent;
        }
    }
}
