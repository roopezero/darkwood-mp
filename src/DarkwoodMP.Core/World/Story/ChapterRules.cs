namespace DarkwoodMP.World.Story
{
    /// A new chapter: when the host tells the joiners, whether the game
    /// builds it, and the joiner's wait for it (Chapters).
    public static class ChapterRules
    {
        /// How long a joiner waits for the host's chapter (realtime) before
        /// it lets its player go on.
        public const float WaitFor = 300f;

        /// Whether the host sends ChapterStart (Chapters.SendChapterStart):
        /// only the host, with at least one peer, and once. With no peer
        /// nothing is marked, not even the chapter as run.
        public static bool SendsChapterStart(bool host, int peers, bool sent)
        {
            return host && peers != 0 && !sent;
        }

        /// What happens to Controller.generateChapter
        /// (Chapters.BeforeGenerate).
        public static GenerateStep BeforeGenerate(in GenerateFacts facts)
        {
            if (!facts.InSession || !facts.NewChapter)
            {
                return GenerateStep.Run;
            }

            if (facts.IsClient)
            {
                return GenerateStep.Skip;
            }

            return GenerateStep.Start;
        }

        /// Whether the host's next chapter is ready (Chapters.Update):
        /// Generating and WorldShare.WorldReady.
        public static bool ChapterReady(bool generating, bool ready)
        {
            return generating && ready;
        }

        /// A ChapterStart came (Chapters.OnChapterStart): one wait at a
        /// time, and a repeat moves its deadline to WaitFor from now.
        public static ChapterWait Extend(Seconds until, Seconds now)
        {
            var waiting = until > now;
            return new ChapterWait
            {
                Until = new Seconds(now.Value + WaitFor),
                Starts = !waiting,
            };
        }

        /// One frame of the wait (Chapters.ChapterWait). The deadline is
        /// checked first.
        public static ChapterWaitStep Wait(in ChapterWaitFacts facts)
        {
            if (!(facts.Now < facts.Until))
            {
                return ChapterWaitStep.TimedOut;
            }

            if (facts.SceneChanged || !facts.InWorld)
            {
                return ChapterWaitStep.Over;
            }

            return ChapterWaitStep.Waits;
        }
    }
}
