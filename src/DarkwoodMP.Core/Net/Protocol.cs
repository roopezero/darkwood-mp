namespace DarkwoodMP.Net
{
    public static class Protocol
    {
        /// <summary>
        /// Bump whenever the wire format changes; a host refuses other
        /// versions.
        /// </summary>
        public const ushort Version = 31;
        public const string Game = "darkwood";
        /// <summary>
        /// Most players in one game, the host included: the host and four
        /// friends (map4 with MAP_CLIENTS=4 plays five).
        /// </summary>
        public const int MaxPlayers = 5;
        /// <summary>
        /// A longer message is an error and ends the connection (as goasis's
        /// MaxMessage).
        /// </summary>
        public const int MaxMessage = 32 << 20;
    }
}
