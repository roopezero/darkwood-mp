namespace DarkwoodMP.World.Scripted
{
    /// Which steps of a scripted event run in this game right now (the
    /// plugin's ScriptedEvents.Part, with the same values).
    public enum EventPart : byte
    {
        /// All of them: nothing shared is going on.
        All = 0,

        /// A joiner's player set it off: its own part and the shared part.
        PlayerAndShared = 1,

        /// The host, for a joiner's event: the host's part and the shared
        /// part.
        HostAndShared = 2,

        /// Another player's event: only the shared part.
        SharedOnly = 3,

        /// The host's player came into (or went out of) a place the world
        /// already had running for another player: only its own part.
        PlayerOnly = 4,

        /// The host's world set it off by itself and our player is near it:
        /// the shared part, and of the player's own part only the messages.
        SharedAndMessages = 5,

        /// Our player set off a once-only trigger that already fired for
        /// another player: only the steps a later player gets.
        LaterPlayer = 6,
    }
}
