namespace DarkwoodMP.Sync.Players
{
    /// What the host does with a client's reported position.
    public enum StateMove : byte
    {
        /// Nothing: a client, or a dropped state.
        None = 0,

        /// MoveCheck.Check: the position may be put back.
        Check,

        /// MoveCheck.Forget: a spectator goes along with whoever it
        /// watches, so it isn't checked and starts afresh if it plays.
        Forget,
    }
}
