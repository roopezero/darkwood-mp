namespace DarkwoodMP.World.Mornings
{
    /// An end waiting for our dialogue with the visitor, each frame.
    public readonly struct PendingFacts
    {
        /// An end is waiting.
        public bool Pending { get; init; }

        /// Singleton<Controller>.Instance exists.
        public bool HasController { get; init; }

        /// The UI's dialogueWindow exists.
        public bool HasWindow { get; init; }

        /// Player.Instance exists.
        public bool HasPlayer { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// We still talk to the visitor (MorningRules.Talking).
        public bool Talking { get; init; }
    }
}
