namespace DarkwoodMP.World
{
    /// What a game knows when one of the world's messages arrives (eggs,
    /// infection, fire, fuel, building): the session, the world and who
    /// sent it.
    public readonly struct ArrivalFacts
    {
        /// There is a session (Session.Instance isn't null).
        public bool Session { get; init; }

        /// The game is in a world (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// This game joined someone else's (Session.IsClient).
        public bool Client { get; init; }

        /// The message came from the host (from == Session.HostId).
        public bool FromHost { get; init; }
    }
}
