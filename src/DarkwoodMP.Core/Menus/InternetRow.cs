namespace DarkwoodMP.Menus
{
    /// The internet address a Direct host is shown to share, with its
    /// note (DirectRules.Internet). No address means the window offers to
    /// look it up instead.
    public readonly struct InternetRow
    {
        /// "ip:port", or null when no address is known yet.
        public string Address { get; init; }

        /// What the row says beside it: "internet", or "internet: forward
        /// the port".
        public string Note { get; init; }
    }
}
