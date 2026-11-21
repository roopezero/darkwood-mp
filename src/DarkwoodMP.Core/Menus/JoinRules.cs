namespace DarkwoodMP.Menus
{
    /// Which servers a player may join from the Direct section, and how
    /// (DirectPanel: CanJoin, Connect and the browser's double click).
    public static class JoinRules
    {
        /// Two clicks on the same server closer than this are a double
        /// click, which joins it.
        public static readonly Seconds DoubleClick = new Seconds(0.4f);

        /// A server may be joined unless it answered with another version.
        /// One that hasn't answered may be tried.
        public static bool MayJoin(bool known, bool incompatible) =>
            !known || !incompatible;

        /// Connect: a server that can't be joined, or none, does nothing.
        /// One that needs a password, called with none, opens the password
        /// window, before the host is checked, so a host gets the window
        /// and then nothing. A host does nothing. Else join, leaving the
        /// game the player is in first.
        ///
        /// The shell then also leaves the TCP backend when it is still in
        /// a session (TcpBackend.InSession, read after the first leave:
        /// still connecting), as DirectPanel.Connect does.
        public static ConnectPlan Connect(in ConnectFacts join)
        {
            if (join.Address == null
                || !MayJoin(join.Known, join.Incompatible))
            {
                return default;
            }

            if (!join.PasswordGiven && join.NeedsPassword)
            {
                return new ConnectPlan { Step = ConnectStep.AskPassword };
            }

            if (join.InGame && join.Host)
            {
                return default;
            }

            return new ConnectPlan
            {
                Step = ConnectStep.Join,
                LeaveFirst = join.InGame,
            };
        }

        /// The click is a double click: on the server clicked last, less
        /// than 0.4 s (unscaled) after that click.
        public static bool IsDoubleClick(in ClickFacts click) =>
            click.Last == click.Address
            && click.Now - click.LastAt < DoubleClick;
    }
}
