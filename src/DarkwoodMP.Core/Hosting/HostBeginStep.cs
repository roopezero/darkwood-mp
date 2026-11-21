namespace DarkwoodMP.Hosting
{
    /// How the host box starts hosting once the world is ready.
    public enum HostBeginStep : byte
    {
        /// A session is running already: stop, and say nothing.
        InSession,

        /// The player can't host this world: stop, and log
        /// "[start game] can't host: " and the refusal's words.
        CantHost,

        /// Host a Steam lobby.
        Steam,

        /// Host with Direct (Session.LastRefusal cleared first).
        Direct,
    }
}
