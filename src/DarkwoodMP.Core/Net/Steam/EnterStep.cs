namespace DarkwoodMP.Net.Steam
{
    /// What to do with a lobby we entered.
    public enum EnterStep : byte
    {
        /// Stay in: set Id, the host and Status, and raise OnEntered.
        Enter,

        /// Steam failed: Status "Failed to join lobby ({response})".
        Failed,

        /// Told a second time, or the lobby we just refused: nothing.
        Ignore,

        /// The host runs another protocol: leave, remember the lobby as
        /// refused, and set Status to LobbyRules.OtherVersion's words.
        OtherVersion,
    }
}
