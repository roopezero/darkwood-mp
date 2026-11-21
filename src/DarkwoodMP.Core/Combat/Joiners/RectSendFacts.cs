namespace DarkwoodMP.Combat.Joiners
{
    /// What a client looks at before it sends its view rect
    /// (ViewRects.Update).
    public readonly struct RectSendFacts
    {
        /// Session.Instance isn't null and is a client.
        public bool Client { get; init; }

        /// The client plays the host's world (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// The game's Controller exists.
        public bool Controller { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }
    }
}
