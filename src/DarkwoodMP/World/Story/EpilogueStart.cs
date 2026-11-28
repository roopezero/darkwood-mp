namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// A new game started at the epilogue (Start Game's "Start at:
    /// Epilogue"), for trying the ending together without playing the whole
    /// game. The game starts as chapter 1 with the prologue skipped, and its
    /// SaveBook record is marked. The host's Multiplayer window then offers
    /// "Start the epilogue": the others join first (nobody can join once the
    /// epilogue plays), and the button sets the chosen story flags (the
    /// Talking Tree burned or not), sends them to the joiners at once
    /// (FlagsSync.FlushNow) and runs the game's own last step, startDream
    /// into the epilogue, which takes every joiner in (EpilogueSync).
    /// Rules: EpilogueStartRules.
    /// </summary>
    internal sealed class EpilogueStart
    {
        /// Whether the Multiplayer window shows the button.
        public bool Shown => EpilogueStartRules.Shows(FactsOf());

        /// Whether the epilogue may start now, or why not.
        public Verdict MayStart() => EpilogueStartRules.MayStart(FactsOf());

        /// "Start the epilogue": sets the flags and runs the step, if it may.
        public Verdict Start()
        {
            Verdict verdict = MayStart();
            if (verdict.Refused)
            {
                Plugin.Log.LogInfo(
                    "[epilogue start] can't start: "
                        + Refusals.Text(verdict));
                return verdict;
            }

            int slot = Core.currentProfile.id;
            TreeFate tree = SaveBook.TreeAt(slot);
            var flags = Singleton<Flags>.Instance;
            foreach (string name in EpilogueStartRules.Flags(tree))
            {
                flags.setFlag(name, activeModifier: true);
            }

            CoopSession coop = Coop.Current;
            int joiners = 0;
            if (coop != null)
            {
                coop.Story.FlushNow();
                joiners = coop.Net.Peers.Count;
            }

            Plugin.Log.LogInfo(
                $"[epilogue start] starting the epilogue (the Tree {tree}, "
                    + $"{joiners} joiners)");
            GameEvent step = Chapters.StepFor(StoryKind.Epilogue, 0);
            Singleton<Controller>.Instance.StartCoroutine(
                step.fire(Player.Instance.gameObject));
            return verdict;
        }

        private static EpilogueStartFacts FactsOf()
        {
            bool atEpilogue = false;
            if (Core.currentProfile != null && WorldShare.InWorld)
            {
                atEpilogue = SaveBook.AtEpilogue(Core.currentProfile.id);
            }

            var session = Plugin.Instance.Host.Session;
            Dreams dreams = Singleton<Dreams>.Instance;
            bool dreaming = false;
            if (dreams != null)
            {
                dreaming = dreams.dreaming || dreams.wantToDream;
            }

            bool inEpilogue = false;
            if (Player.Instance != null)
            {
                inEpilogue = Player.Instance.inEpilogue;
            }

            CoopSession coop = Coop.Current;
            if (coop != null && coop.Epilogue.Playing)
            {
                inEpilogue = true;
            }

            return new EpilogueStartFacts
            {
                AtEpilogue = atEpilogue,
                Joiner = session.IsClient,
                InWorld = WorldShare.WorldReady,
                Dreaming = dreaming,
                InEpilogue = inEpilogue,
            };
        }
    }
}
