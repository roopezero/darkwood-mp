namespace DarkwoodMP.Net.Steam
{
    /// What to do this frame about a peer waiting to be closed.
    public enum CloseStep : byte
    {
        /// Not yet.
        Wait,

        /// Its queue is empty: close it at SteamSendRules.CloseAt(now).
        Arm,

        /// Close the session now.
        Close,
    }
}
