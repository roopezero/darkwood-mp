namespace DarkwoodMP.Hosting
{
    /// HostRules.Begin's answer: the step, and for CantHost why.
    public readonly struct HostBegin
    {
        public HostBeginStep Step { get; init; }

        /// Why the player can't host, for CantHost. Allow otherwise.
        public Verdict Why { get; init; }
    }
}
