namespace DarkwoodMP.World.Objects
{
    /// What the host knows about a thing a client's player took off the
    /// ground (Removed).
    public readonly struct PickupFacts
    {
        /// It is in the host's world (ObjRef.Resolve<Transform>).
        public bool Exists { get; init; }

        /// It has a Lure.
        public bool Lure { get; init; }

        /// It has a JournalNoteReference.
        public bool Note { get; init; }

        /// It has a DeathDrop.
        public bool DeathDrop { get; init; }

        /// The requester and the thing. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
