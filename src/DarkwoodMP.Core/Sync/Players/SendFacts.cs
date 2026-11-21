namespace DarkwoodMP.Sync.Players
{
    /// The session, when our game is about to send our player's state
    /// (PlayerSync.Send).
    public readonly struct SendFacts
    {
        /// Session.Active.
        public bool Active { get; init; }

        /// Session.Connected.
        public bool Connected { get; init; }

        /// Our character isn't placed yet (Characters.Placing,
        /// false with no Characters).
        public bool Placing { get; init; }
    }
}
