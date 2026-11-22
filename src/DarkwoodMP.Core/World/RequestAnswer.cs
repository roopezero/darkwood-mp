namespace DarkwoodMP.World
{
    /// What the host has decided about the request it is handling: the
    /// Verdict message sends ok (not refused), the refusal's words and the
    /// result. The zero value is "no request being handled".
    public readonly struct RequestAnswer
    {
        readonly string _result;

        /// Inside a request's handler (Authority.Handling).
        public bool Handling { get; init; }

        /// Who sent the request (Authority.From).
        public ulong From { get; init; }

        /// The first refusal, or Allow.
        public Verdict Verdict { get; init; }

        /// What came of it, for the client. Never null: "" stands for none.
        public string Result
        {
            get
            {
                if (_result == null)
                {
                    return "";
                }

                return _result;
            }

            init => _result = value;
        }
    }
}
