namespace DarkwoodMP.World.Rolls
{
    /// Whether the host sends its night scenario or weather now
    /// (HostRollClock).
    public enum RollSend : byte
    {
        /// Not now.
        None = 0,

        /// It changed since it was last sent.
        Changed,

        /// The same as last sent, but RollRules.Resend has passed.
        Again,
    }
}
