namespace DarkwoodMP.Presence
{
    /// What our Steam rich presence says (Plugin.PresenceText).
    public readonly struct PresenceFacts
    {
        /// The mod's version (PluginInfo.Version).
        public string Version { get; init; }

        /// A world is loaded and being played (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// The world's day (Controller.day).
        public int Day { get; init; }

        /// The world's chapter (WorldGenerator.chapterID), 0 without a
        /// world generator.
        public int Chapter { get; init; }

        /// The biome's name (PresenceRules.BiomeName), empty for none.
        public string Biome { get; init; }

        /// A session is running (Session.Active).
        public bool InSession { get; init; }

        /// We host it (Session.IsHost).
        public bool IsHost { get; init; }

        /// Players in it, us included (Session.PlayerCount).
        public int Players { get; init; }

        /// The most it takes (Session.Backend.MaxPlayers).
        public int MaxPlayers { get; init; }

        /// The host's name (Session.Names[Session.HostId]), null when it
        /// isn't known.
        public string HostName { get; init; }
    }
}
