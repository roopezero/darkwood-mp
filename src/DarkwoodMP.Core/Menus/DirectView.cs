namespace DarkwoodMP.Menus
{
    /// Which Direct section the Multiplayer window shows
    /// (DirectRules.View).
    public enum DirectView
    {
        /// In no game: the status, the join buttons and the host fields.
        Idle = 0,

        /// Hosting a Steam game: "In a Steam game." and nothing else.
        SteamHost,

        /// In someone's Steam game: a line saying joining here leaves it,
        /// and the join buttons.
        SteamClient,

        /// Hosting, joining or in a Direct (TCP) game: its status, the
        /// addresses to share or Leave.
        DirectGame,
    }
}
