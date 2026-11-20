namespace DarkwoodMP.World.Story
{
    /// StoryRules.Decide's answer about a joiner's story step.
    public readonly struct StoryAnswer
    {
        public StoryOutcome Outcome { get; init; }

        /// The refusal, when Outcome is Refuse; Allow otherwise.
        public Verdict Verdict { get; init; }

        /// Why it is done or waits, when Outcome is Done or Wait; null
        /// otherwise.
        public string Reason { get; init; }

        /// The reach has been checked: the held request's new
        /// ReachChecked.
        public bool ReachChecked { get; init; }

        /// The words for the log and the result: Reason, or the
        /// refusal's text.
        public string Words
        {
            get
            {
                if (Outcome == StoryOutcome.Refuse)
                {
                    return Refusals.Text(Verdict);
                }

                return Reason;
            }
        }
    }
}
