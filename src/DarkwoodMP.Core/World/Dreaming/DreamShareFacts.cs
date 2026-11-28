namespace DarkwoodMP.World.Dreaming
{
    /// The session's side of a shared dream (DreamSync).
    public readonly struct DreamShareFacts
    {
        /// ModHost.Session is there.
        public bool HasSession { get; init; }

        /// We host the session.
        public bool IsHost { get; init; }

        /// NetEnemies.ClientMode: we play in the host's world.
        public bool ClientMode { get; init; }

        /// Hideouts.Together: everyone shares one hideout.
        public bool Together { get; init; }

        /// The message came from the host (from == Session.HostId).
        public bool FromHost { get; init; }

        /// A dream is shared (DreamSync.Shared isn't null).
        public bool Shared { get; init; }

        /// The shared dream's end was sent or replayed (DreamSync._ended).
        public bool Ended { get; init; }

        /// Coop.Guards.Replaying: applying another game's change.
        public bool Replaying { get; init; }

        /// Host: our level just started a level dream
        /// (DreamSync._hostLevelDream).
        public bool Marked { get; init; }

        /// The game drew a preset (getPreset's result isn't null).
        public bool HasPreset { get; init; }

        /// Client: a host's dream is waiting to start (_pendingStart).
        public bool PendingStart { get; init; }

        /// EpilogueSync.Playing.
        public bool EpiloguePlaying { get; init; }

        /// EpilogueSync.OutcomeAllowed for the outcome a DreamEnd names.
        public bool OutcomeAllowed { get; init; }

        /// The DreamEnd names the dream we share; a DreamStart, the dream
        /// our player is in.
        public bool SameDream { get; init; }
    }
}
