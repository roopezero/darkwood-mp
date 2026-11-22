namespace DarkwoodMP.Net.Steam
{
    /// What a lobby member change is to us.
    public enum LobbyChange : byte
    {
        /// Another lobby's: nothing.
        Ignore,

        /// Someone came in: log "{name} joined the lobby".
        Joined,

        /// Anything else counts as leaving: log "{name} left the lobby"
        /// and raise OnMemberLeft.
        Left,
    }
}
