namespace DarkwoodMP.World.Dragging
{
    /// The host's answer to a claim, done in this order: the host's place
    /// to the claimer (SendHere) when PutBack, then the refusal or the
    /// agreement. The Verdict goes out when the request's handler ends, so
    /// the place always reaches the claimer before it.
    public readonly struct ClaimOutcome
    {
        public ClaimAnswer Answer { get; init; }

        /// The refusal, when Refused.
        public Verdict Verdict { get; init; }

        /// The claimer's copy goes where the thing is in the host's world.
        public bool PutBack { get; init; }
    }
}
