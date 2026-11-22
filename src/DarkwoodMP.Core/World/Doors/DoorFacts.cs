namespace DarkwoodMP.World.Doors
{
    /// What the host knows about a door when a client asks to change it
    /// (the plugin's Doors.Allowed).
    public readonly struct DoorFacts
    {
        /// The door is in the host's world (ObjRef.Resolve of a Door).
        public bool Exists { get; init; }

        /// The requester and the door, for the reach check
        /// (Authority.InReach): Target is the door's position, What its
        /// name. The rule sets the reach itself.
        public ReachFacts Requester { get; init; }

        /// Door.destroyed.
        public bool Destroyed { get; init; }

        /// Door.barricaded.
        public bool Barricaded { get; init; }

        /// Door.blocked: only the story clears it.
        public bool Blocked { get; init; }

        /// The door has a Locked component.
        public bool HasLock { get; init; }

        /// Locked.locked; ignored without HasLock.
        public bool Locked { get; init; }

        /// The lock's key type is in the host's journal
        /// (WorldActions.HasKey).
        public bool HasKey { get; init; }

        /// Locked.keyType, for the refusal's words.
        public string KeyType { get; init; }
    }
}
