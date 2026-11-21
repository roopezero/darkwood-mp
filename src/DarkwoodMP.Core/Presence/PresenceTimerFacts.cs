namespace DarkwoodMP.Presence
{
    /// What decides whether the presence is worked out again this frame
    /// (Plugin.Update, before UpdatePresence).
    public readonly struct PresenceTimerFacts
    {
        /// Steam's backend is up (Plugin's _steam isn't null).
        public bool SteamUp { get; init; }

        /// --dwmp-test-presence was given: log the text even without Steam.
        public bool Logged { get; init; }

        /// Unscaled time now (Time.unscaledTime).
        public Seconds Now { get; init; }

        /// When it's due (Plugin._nextPresence), 0 at the start.
        public Seconds Next { get; init; }
    }
}
