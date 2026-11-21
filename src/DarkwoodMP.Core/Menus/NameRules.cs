namespace DarkwoodMP.Menus
{
    /// When the player's name may change, and what the menus say about it
    /// (OptionsWindow.DrawMultiplayer, StartGameWindow.DrawHost and the
    /// server browser's status line in DirectPanel.DrawBrowser).
    public static class NameRules
    {
        /// Options: the name can't change while the player is in a game
        /// (a session is active). Start Game's host box doesn't ask this:
        /// see AppliesTypedName.
        public static Verdict MayRename(bool inGame)
        {
            if (inGame)
            {
                return Verdict.Refuse(Refusal.NameLocked);
            }

            return Verdict.Allow;
        }

        /// Options, under the name field while it may change: the name the
        /// others see (Session.LocalName, "" with no session).
        public static string OthersSee(string shown) =>
            "Others see you as " + shown + ". Left empty, it's your Steam"
            + " name, or your computer's user name.";

        /// Start Game: a name typed in the host box is used while the box
        /// is ticked and the name differs from the one in use
        /// (Session.LocalName). There is no session check here, unlike
        /// Options, but the box can't be ticked in a game.
        public static bool AppliesTypedName(
            bool ticked,
            string typed,
            string current) =>
            ticked && typed != current;

        /// The server browser, beside its status line: where to set a name,
        /// while no game is on and the setting is blank (the Steam or the
        /// computer's user name, "local", is used then). Null when there
        /// is nothing to say.
        public static string BrowserHint(
            bool inGame,
            string setting,
            string local)
        {
            if (inGame || !string.IsNullOrWhiteSpace(setting))
            {
                return null;
            }

            return "You're " + local + ": set a name in Options";
        }
    }
}
