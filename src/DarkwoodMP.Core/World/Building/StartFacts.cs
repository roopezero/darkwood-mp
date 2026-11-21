namespace DarkwoodMP.World.Building
{
    /// What a game knows as Constructible.construct starts.
    public readonly struct StartFacts
    {
        /// The joiner is building its copy after the host's yes
        /// (Construction.Confirming).
        public bool Confirming { get; init; }

        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// The construct call's manual argument: the player built it.
        public bool Manual { get; init; }

        /// A test step builds it as the player (WorldActions.TestAsPlayer).
        public bool TestAsPlayer { get; init; }

        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// The client asked the host about this one and has no answer yet
        /// (Construction.IsWaiting).
        public bool Waiting { get; init; }
    }
}
