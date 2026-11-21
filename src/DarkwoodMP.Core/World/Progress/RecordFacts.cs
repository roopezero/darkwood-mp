namespace DarkwoodMP.World.Progress
{
    /// What decides whether the host's game keeps the shared record, and
    /// whether it starts one (Progression.HostSaved).
    public readonly struct RecordFacts
    {
        /// ProgressSideRules.Solo.
        public bool Solo { get; init; }

        /// A session is there and we host it.
        public bool IsHost { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// WorldShare.Loading.
        public bool Loading { get; init; }

        /// Player.Instance is there.
        public bool HasPlayer { get; init; }

        /// NetEnemies.ClientMode.
        public bool ClientMode { get; init; }

        /// The file's path is known (SaveManager and Core.currentProfile).
        public bool HasPath { get; init; }

        /// The record is there once the file has been read.
        public bool HasRecord { get; init; }

        /// The caller may start a record (HostSaved's start).
        public bool Start { get; init; }

        /// Playing alone, the caller is our own player (HostSaved's alone:
        /// a reset by "host").
        public bool Alone { get; init; }

        /// How many players are connected (Session.Peers.Count).
        public int Peers { get; init; }
    }
}
