namespace DarkwoodMP.World.Places
{
    /// What becomes of Location.leave on the host.
    public enum LeaveStep : byte
    {
        /// Not kept: the game's leave runs, and its quiet mark is dropped.
        Leave = 0,

        /// Kept: the game's leave doesn't run.
        Stay,

        /// Kept, and our player walked out: its exit events run and its
        /// timeLeft is set, but the game's leave doesn't run.
        StayWithExitEvents,
    }
}
