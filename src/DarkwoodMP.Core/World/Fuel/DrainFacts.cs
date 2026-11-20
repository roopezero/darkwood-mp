namespace DarkwoodMP.World.Fuel
{
    /// What a game knows when one of its generators burns fuel.
    public readonly struct DrainFacts
    {
        /// There is a session.
        public bool Session { get; init; }

        /// It is a client (Session.IsClient).
        public bool Client { get; init; }

        /// It is the host (Session.IsHost).
        public bool Host { get; init; }

        /// Player.Instance is there.
        public bool Player { get; init; }

        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// There are stand-ins for other players (Proxies.Any).
        public bool Proxies { get; init; }

        /// The players' characters are known (Characters.Instance).
        public bool Characters { get; init; }
    }
}
