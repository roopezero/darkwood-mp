namespace DarkwoodMP.World.Clock
{
    /// What a client does with its own clock each frame.
    public enum ClockHold
    {
        /// Nothing to do.
        Leave,

        /// Turn Controller.DoUpdateTime off if it's on (the game turns it
        /// back on at times), and remember the clock is stopped.
        Stop,

        /// No longer a client's copy: turn DoUpdateTime back on and forget
        /// it was stopped.
        Restart,

        /// As Restart, with no Controller to turn on: only forget.
        Forget,
    }
}
