namespace DarkwoodMP.World.Scripted
{
    /// A trigger's events start running (GameEvents.fire), for whether to
    /// share them.
    public readonly struct FiringFacts
    {
        /// A trigger to share is firing (ScriptedEvents.Firing isn't
        /// null), with its EventTriggers and its index there.
        public bool Firing { get; init; }

        public bool HasTriggers { get; init; }

        public int Index { get; init; }

        /// The host's world set it off by itself.
        public bool World { get; init; }

        /// Host: the client behind that world trigger, or 0.
        public ulong By { get; init; }

        /// The part being run now (ScriptedEvents.Current).
        public EventPart Current { get; init; }

        /// The events: GameEvents.fired and multipleFire.
        public bool Fired { get; init; }

        public bool MultipleFire { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// This game is a client.
        public bool Client { get; init; }
    }
}
