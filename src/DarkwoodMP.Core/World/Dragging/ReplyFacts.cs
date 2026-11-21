namespace DarkwoodMP.World.Dragging
{
    /// The host's answer to our claim (the Request's callback).
    public readonly struct ReplyFacts
    {
        /// Our player still drags the thing the claim was for.
        public bool Same { get; init; }

        /// The host agreed.
        public bool Ok { get; init; }

        /// The refusal's words, when not Ok.
        public string Reason { get; init; }
    }
}
