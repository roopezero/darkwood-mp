namespace DarkwoodMP.World.Scripted
{
    /// What an EventFired message is about (its first byte).
    public enum EventKind : byte
    {
        /// A player set off a trigger.
        Trigger = 1,

        /// A world event by name (a dialogue's outcome, an animation's).
        WorldEvent = 2,

        /// The host's world set off a trigger by itself.
        WorldTrigger = 3,

        /// A player left an area trigger.
        TriggerExit = 4,

        /// A client asks for its own part of a once-only trigger.
        PlayerPart = 5,

        /// A world trigger a client caused: its player part is theirs.
        WorldTriggerCaused = 6,
    }
}
