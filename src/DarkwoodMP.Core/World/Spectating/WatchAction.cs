namespace DarkwoodMP.World.Spectating
{
    /// What watching does this frame.
    public enum WatchAction : byte
    {
        /// Watch a player: pick one, follow it.
        Watch = 0,

        /// The epilogue with nobody to watch: stay on the body.
        Stay,

        /// Nobody, and out of a world (it is being replaced): end.
        Ended,

        /// Nobody, and the session is gone: end, then permadeath.
        SessionLost,

        /// Nobody for over SpectatorRules.NobodyFor: end, then permadeath.
        NobodyLeft,

        /// Nobody for now: wait.
        Waits,
    }
}
