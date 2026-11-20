namespace DarkwoodMP.World.Throws
{
    /// What becomes of a copy of another's throw (ThrowRules.Landing).
    public readonly struct ThrowCopyFacts
    {
        /// The distance the message sent (below 0 for a drop).
        public float Distance { get; init; }

        /// The item can be picked up again after it's thrown
        /// (InvItem.recoverableAfterThrown).
        public bool Recoverable { get; init; }

        public bool IsHost { get; init; }
    }
}
