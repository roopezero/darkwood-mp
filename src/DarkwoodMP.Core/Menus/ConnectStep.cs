namespace DarkwoodMP.Menus
{
    /// What Connect does with a server (JoinRules.Connect).
    public enum ConnectStep
    {
        /// Nothing, and silently: no server, one that can't be joined, or
        /// the player is hosting.
        Nothing = 0,

        /// Open the password window for it, and join from there.
        AskPassword,

        /// Join it.
        Join,
    }
}
