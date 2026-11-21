namespace DarkwoodMP.Net.Steam
{
    /// One peer waiting to be closed once what was sent to it is gone
    /// (SteamTransport.CloseSent).
    public readonly struct CloseFacts
    {
        /// Messages still wait for room for it (it's in _pending).
        public bool Waiting { get; init; }

        /// Its close time is set (_closing's value isn't -1).
        public bool Armed { get; init; }

        /// When to close it, once Armed.
        public Seconds At { get; init; }

        /// Unscaled time now (Time.unscaledTime).
        public Seconds Now { get; init; }
    }
}
