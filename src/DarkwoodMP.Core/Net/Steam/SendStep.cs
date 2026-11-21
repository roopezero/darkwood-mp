namespace DarkwoodMP.Net.Steam
{
    /// What to do after Steam's answer to a reliable message.
    public enum SendStep : byte
    {
        /// It went: return true.
        Sent,

        /// No room: queue it for later (SteamSendRules.MayQueue).
        Queue,

        /// Log "Send to {to} failed: {result}" and return false.
        Failed,
    }
}
