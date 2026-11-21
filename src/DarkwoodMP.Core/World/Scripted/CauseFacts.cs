namespace DarkwoodMP.World.Scripted
{
    /// Host: who is behind a world trigger set off now.
    public readonly struct CauseFacts
    {
        /// The client whose action is being applied now
        /// (ScriptedEvents.Cause), or 0.
        public ulong Cause { get; init; }

        /// This game is the host (a session exists and IsHost).
        public bool Host { get; init; }

        /// Cause is still one of the session's peers.
        public bool Present { get; init; }

        /// The trigger's type is onDeath or refreshWhenTurnedOn.
        public bool Caused { get; init; }
    }
}
