namespace DarkwoodMP.World.Spectating
{
    /// One round of a dead joiner's wait for someone to watch.
    public enum DeadWaitStep : byte
    {
        /// Wait half a second more.
        Waits = 0,

        /// Out of a world, or watching already: stop.
        Stops,

        /// Watch the first of the players.
        Begins,

        /// Nobody came: the game's permadeath.
        Permadeath,
    }
}
