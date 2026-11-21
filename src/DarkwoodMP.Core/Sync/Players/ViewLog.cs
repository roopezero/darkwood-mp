namespace DarkwoodMP.Sync.Players
{
    /// What the view watch logged last (ViewWatch._logged and _loggedAt).
    /// Zero is the start: nothing logged, at 0.
    public readonly struct ViewLog
    {
        /// The hiders as logged, "; " between them; null or "" for none.
        public string Hiders { get; init; }

        /// When (unscaled).
        public Seconds At { get; init; }
    }
}
