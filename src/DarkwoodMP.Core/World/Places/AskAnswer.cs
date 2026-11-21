namespace DarkwoodMP.World.Places
{
    /// How the host answers a LocationAsk. Every answer but Ignore and
    /// NoSuchLocation is remembered and sent as a LocationSlot.
    public enum AskAnswer : byte
    {
        /// Not the host, not in a world, no OutsideLocations, or no name.
        Ignore = 0,

        /// The spot promised or used before.
        Known,

        /// Where our loaded copy stands, turned as it is
        /// (LocationSlotRules.TurnOf its y angle).
        Loaded,

        /// Not a location of the build: no answer, a warning. The joiner
        /// waits out LocationSlotRules.AskTimeout.
        NoSuchLocation,

        /// The next free spot and a turn the shell rolls; the game's count
        /// goes up by one, and we load it too unless it is a dream.
        NewSpot,
    }
}
