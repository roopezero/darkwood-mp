namespace DarkwoodMP.World.Scripted
{
    /// <summary>
    /// What an EventTrigger.fire or GameEvent.fire coroutine was started
    /// with (EventParts.Remember).
    /// </summary>
    internal sealed class Started
    {
        public EventPart Part;

        /// <summary>The host's By when it was started.</summary>
        public ulong By;

        /// <summary>
        /// EventTrigger.fire only: our own player (or, on the host, the
        /// world) set it off, and it's to be shared when it fires.
        /// </summary>
        public bool ByPlayer;

        /// <summary>
        /// The host's world set it off by itself (sent as kind 3).
        /// </summary>
        public bool World;

        public EventTriggers Triggers;

        public int Index;
    }
}
