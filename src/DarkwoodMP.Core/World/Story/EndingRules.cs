namespace DarkwoodMP.World.Story
{
    /// The game's end: when the host ends it, and what a joiner does
    /// (Chapters, EpilogueSync).
    public static class EndingRules
    {
        /// How long a joiner tries to start the host's epilogue (realtime),
        /// once a second.
        public const float TryFor = 30f;

        /// How long it then waits for its player to be in the epilogue.
        public const float EnterFor = 60f;

        /// How long a joiner alone waits for its player to be in the
        /// epilogue (Chapters.ShowEnding).
        public const float AloneEnterFor = 45f;

        /// The ending the host starts (Chapters.HostEnding): only the host,
        /// once. None when it starts nothing.
        public static EndKind HostEnds(bool host, bool sent, EndKind kind)
        {
            if (!host || sent)
            {
                return EndKind.None;
            }

            return kind;
        }

        /// Whether a dream being prepared ends the game
        /// (Chapters.OnPrepareDream): only the epilogue's.
        public static bool DreamEnds(string preset)
        {
            return preset == StoryRequestRules.EpilogueDream;
        }

        /// What a joiner does with the host's GameEnd. A kind other than
        /// the epilogue's is an ending alone, whatever its phase.
        public static GameEndStep GameEnd(in GameEndFacts facts)
        {
            if (!facts.FromHost)
            {
                return GameEndStep.Ignored;
            }

            if (facts.Kind != (byte)EndKind.Epilogue)
            {
                return GameEndStep.Solo;
            }

            if (facts.Phase == 0)
            {
                if (facts.InWorld)
                {
                    return GameEndStep.JoinEpilogue;
                }

                return GameEndStep.BeginEpilogue;
            }

            var credits = !facts.Playing || facts.AtCredits;
            if (!credits && (facts.InWorld || facts.InEpilogue))
            {
                return GameEndStep.LeaveToCredits;
            }

            return GameEndStep.Leave;
        }

        /// Whether a joiner that left sees the ending in its copy of the
        /// host's world (Chapters.SoloEnding): in a world, with saves
        /// redirected (SaveRedirect.Active).
        public static bool ShowsEnding(bool world, bool redirected)
        {
            return world && redirected;
        }

        /// Whether a joiner alone, out of the session, can go into the
        /// epilogue (Chapters.StartEpilogue): not while dreaming, nor in an
        /// outside location.
        public static bool MayStartAlone(in StartFacts facts)
        {
            if (!facts.Dreams.Exists
                || !facts.HasPlayer
                || facts.Dreams.Dreaming)
            {
                return false;
            }

            return !facts.InOutside;
        }

        /// Whether a joiner can go into the host's epilogue now
        /// (EpilogueSync.StartHere): not in or near a dream, not loading,
        /// and no place loading. Unlike MayStartAlone, an outside location
        /// doesn't stop it.
        public static bool MayStartShared(in StartFacts facts)
        {
            var dreams = facts.Dreams;
            if (!dreams.Exists
                || !facts.HasPlayer
                || dreams.Dreaming
                || dreams.Prepared
                || dreams.Switching
                || facts.Loading)
            {
                return false;
            }

            return !facts.PlaceLoading;
        }

        /// How the joiner's way into the epilogue ends, after the wait
        /// (EpilogueSync.JoinEpilogue). Our player in the epilogue wins
        /// over Playing having gone false meanwhile. Before the wait the
        /// shell has already stopped when Playing went false.
        public static JoinEnd JoinEnds(bool playing, bool inEpilogue)
        {
            if (inEpilogue)
            {
                return JoinEnd.Joined;
            }

            if (!playing)
            {
                return JoinEnd.Stopped;
            }

            return JoinEnd.Fallback;
        }
    }
}
