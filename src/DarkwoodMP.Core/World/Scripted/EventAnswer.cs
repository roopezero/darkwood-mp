namespace DarkwoodMP.World.Scripted
{
    /// The host's answer to a client's trigger or world event: refused,
    /// allowed, or allowed as all theirs (they are where the host doesn't
    /// run, Authority.AcceptAway with the result "alone").
    public readonly struct EventAnswer
    {
        /// The first refusal, or Allow.
        public Verdict Verdict { get; init; }

        /// Allowed as all theirs: nothing runs on the host.
        public bool Alone { get; init; }
    }
}
