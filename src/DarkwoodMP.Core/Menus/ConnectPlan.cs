namespace DarkwoodMP.Menus
{
    /// What Connect does (JoinRules.Connect).
    public readonly struct ConnectPlan
    {
        public ConnectStep Step { get; init; }

        /// Before joining, leave the game the player is in
        /// (Session.Backend.Leave, logged "Joining {addr}: leaving the
        /// game we're in first"). Only with Join.
        public bool LeaveFirst { get; init; }
    }
}
