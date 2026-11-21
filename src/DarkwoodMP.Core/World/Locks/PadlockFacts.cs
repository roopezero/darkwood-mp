namespace DarkwoodMP.World.Locks
{
    /// What the host knows about a combination padlock when a client says
    /// it opened it (PadlockUnlock).
    public readonly struct PadlockFacts
    {
        /// The padlock is in the host's world (ObjRef.Resolve<Padlock>).
        public bool Exists { get; init; }

        /// Padlock.locked.
        public bool Locked { get; init; }

        /// The action's flag 1: a player entered the code.
        public bool Manual { get; init; }

        /// The code the client's player entered (the action's s).
        public string Code { get; init; }

        /// Padlock.combination.
        public string Combination { get; init; }

        /// The requester and the padlock. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
