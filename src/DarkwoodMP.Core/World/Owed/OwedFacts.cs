namespace DarkwoodMP.World.Owed
{
    /// Whether this game's player is owed its own part of a fired
    /// once-only trigger (OwedRules.Owed). The host's and the joiner's
    /// facts are read only on their own side: reading the host's record
    /// on a joiner would read the file there.
    public readonly struct OwedFacts
    {
        /// There is a trigger (it isn't null).
        public bool Trigger { get; init; }

        /// There is a session and it is active.
        public bool Active { get; init; }

        /// This game hosts (Session.IsHost).
        public bool Host { get; init; }

        /// Host: the trigger has an id and the record owes it to the host's
        /// player (OwedRecord.IsHostOwed).
        public bool HostOwed { get; init; }

        /// Joiner: the trigger has its EventTriggers object
        /// (thisEventTriggers isn't null).
        public bool HasObject { get; init; }

        /// Joiner: the object is one only this game has
        /// (WorldActions.OursAlone).
        public bool OursAlone { get; init; }

        /// Joiner: our player had its own part of it in the world loaded
        /// now (OwedParts.OursHad).
        public bool HadHere { get; init; }
    }
}
