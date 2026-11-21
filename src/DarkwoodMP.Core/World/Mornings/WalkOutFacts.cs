namespace DarkwoodMP.World.Mornings
{
    /// A client's Location.OnTriggerExit, around which its morning may end.
    public readonly struct WalkOutFacts
    {
        /// In a morning before the exit (Controller.isAfterNight).
        public bool Before { get; init; }

        /// Singleton<Controller>.Instance exists after it.
        public bool HasController { get; init; }

        /// Still in a morning after it.
        public bool After { get; init; }

        /// There is a session and this game is a client.
        public bool IsClient { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }
    }
}
