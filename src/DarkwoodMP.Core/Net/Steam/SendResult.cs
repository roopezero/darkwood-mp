namespace DarkwoodMP.Net.Steam
{
    /// Steam's answer to one SendMessageToUser, from its EResult.
    public enum SendResult : byte
    {
        /// k_EResultOK.
        Sent,

        /// k_EResultLimitExceeded: Steam's queue for that peer is full.
        Full,

        /// Any other result. The shell logs the EResult's name.
        Failed,
    }
}
