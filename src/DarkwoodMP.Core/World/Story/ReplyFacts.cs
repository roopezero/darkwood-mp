namespace DarkwoodMP.World.Story
{
    /// The host's answer to a joiner's StoryRequest, as the request's
    /// callback gets it.
    public readonly struct ReplyFacts
    {
        /// The host didn't refuse it.
        public bool Ok { get; init; }

        /// The refusal's words, when refused.
        public string Reason { get; init; }

        /// The request's result: "done: ...", "pending: ..." or null.
        public string Result { get; init; }

        /// What was asked for (StoryRules.What).
        public string What { get; init; }
    }
}
