namespace DarkwoodMP.Sync
{
    /// Why the movement check took or kept a position.
    public enum MoveOutcome : byte
    {
        /// Taken: the player moved no faster than MaxSpeed.
        Accepted,

        /// Taken, and the check starts again there: the first position of
        /// a player, or a jump further than Teleport (the game moving them).
        Started,

        /// Taken, and the check starts again there: it is where a trip the
        /// host agreed to lands. The plugin logs it.
        Tripped,

        /// Not taken: faster than MaxSpeed, so they stay where they were.
        PutBack,
    }
}
