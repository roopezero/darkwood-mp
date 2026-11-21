namespace DarkwoodMP.World
{
    /// A Verdict message as a client reads it (VerdictMessage.Read).
    public readonly struct VerdictReply
    {
        /// The number of the request it answers.
        public uint Seq { get; init; }

        /// The host agreed to it.
        public bool Ok { get; init; }

        /// Why it was refused, "" if it wasn't.
        public string Reason { get; init; }

        /// What the host decided, "" if nothing.
        public string Result { get; init; }
    }
}
