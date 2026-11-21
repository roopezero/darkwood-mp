namespace DarkwoodMP.Net.Steam
{
    /// What decides whether Plugin.Update looks for Steam this frame.
    public readonly struct SteamCheckFacts
    {
        /// Steam's backend is up (Plugin's _steam isn't null).
        public bool Up { get; init; }

        /// Steam is missing for good (Plugin._steamUnavailable): no
        /// SteamManager, or the backend threw as it was made.
        public bool Missing { get; init; }

        /// Unscaled time now (Time.unscaledTime).
        public Seconds Now { get; init; }

        /// When the next look is due (Plugin._nextSteamCheck), 0 at first.
        public Seconds Next { get; init; }
    }
}
