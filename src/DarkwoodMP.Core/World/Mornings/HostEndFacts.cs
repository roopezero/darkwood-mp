namespace DarkwoodMP.World.Mornings
{
    /// What the host looks at when its morning ended in its own game.
    public readonly struct HostEndFacts
    {
        /// There is a session and this game is its host.
        public bool IsHost { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }

        /// Singleton<Controller>.Instance exists (its day is remembered).
        public bool HasController { get; init; }

        /// Session.Peers.Count.
        public int Peers { get; init; }
    }
}
