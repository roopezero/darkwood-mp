namespace DarkwoodMP.World.Inventories
{
    /// What to do with a container's whole contents from another game
    /// (Containers.OnContainer).
    public readonly struct ContentsAnswer
    {
        /// Refused: the host sends its own contents back to the client to
        /// roll back to, and does nothing else.
        public Verdict Verdict { get; init; }

        /// Load them into the container here.
        public bool Apply { get; init; }

        /// Pass them on to everyone but the sender.
        public bool Relay { get; init; }
    }
}
