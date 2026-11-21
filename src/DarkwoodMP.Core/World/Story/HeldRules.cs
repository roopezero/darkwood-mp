namespace DarkwoodMP.World.Story
{
    /// Host: the one joiner's story step it holds until its game can run it
    /// (Chapters._held, OnStoryRequest and UpdateHeld).
    public static class HeldRules
    {
        /// A held step runs once it could for this many seconds in a row
        /// (realtime): a dream's end settles.
        public const float Settle = 3f;

        /// Whether a request that must wait repeats the one held: the same
        /// key and the same player. A repeat keeps the held one, with its
        /// reach check and settle time; anything else replaces it.
        public static bool Repeats(HeldStory held, HeldStory asked)
        {
            if (held.Kind == StoryKind.None)
            {
                return false;
            }

            return StoryRequestRules.KeyOf(held.Kind, held.Chapter)
                    == StoryRequestRules.KeyOf(asked.Kind, asked.Chapter)
                && held.From == asked.From;
        }

        /// What to do with the held step this frame. Dropped leaves the
        /// settle state as it was; the joiner isn't told about any outcome.
        public static HeldStep Next(in HeldFacts facts)
        {
            if (!facts.PlayerHere)
            {
                return new HeldStep
                {
                    Action = HeldAction.Dropped,
                    Ready = facts.Ready,
                    ReadySince = facts.ReadySince,
                };
            }

            if (facts.Outcome == StoryOutcome.Wait)
            {
                return default;
            }

            if (facts.Outcome == StoryOutcome.Run)
            {
                var since = facts.ReadySince;
                if (!facts.Ready)
                {
                    since = facts.Now;
                }

                if (facts.Now.Value - since.Value < Settle)
                {
                    return new HeldStep
                    {
                        Action = HeldAction.Keep,
                        Ready = true,
                        ReadySince = since,
                    };
                }

                return new HeldStep { Action = HeldAction.Run };
            }

            if (facts.Outcome == StoryOutcome.Done)
            {
                return new HeldStep { Action = HeldAction.Done };
            }

            return new HeldStep { Action = HeldAction.Refused };
        }
    }
}
