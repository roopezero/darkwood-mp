using UnityEngine;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// A new game started at the epilogue (Start Game's "Start at:
    /// Epilogue"), for trying the ending together without playing the whole
    /// game. The game starts as chapter 1 with the prologue skipped, and its
    /// SaveBook record is marked. A moment after its world is ready (so the
    /// hosting that Start Game asked for is up), Update sets the chosen story
    /// flags (the Talking Tree burned or not), sends them to any joiners at
    /// once (FlagsSync.FlushNow) and runs the game's own last step,
    /// startDream into the epilogue. The game saves on its way into the
    /// first part, so continuing that save goes back into the epilogue by
    /// itself. The others join the epilogue while it plays (EpilogueSync's
    /// late join). Rules: EpilogueStartRules.
    /// </summary>
    internal sealed class EpilogueStart
    {
        /// When the world was first seen ready (realtime; below 0: it isn't).
        private float _readyAt = -1f;

        /// The epilogue was started in this world.
        private bool _started;

        /// Whether the epilogue may start now, or why not.
        public Verdict MayStart() => EpilogueStartRules.MayStart(FactsOf());

        /// Each frame (Plugin.Update): the epilogue starts by itself once the
        /// rules say so, once per world.
        public void Update()
        {
            if (!WorldShare.WorldReady)
            {
                _readyAt = -1f;
                _started = false;
                return;
            }

            if (_readyAt < 0f)
            {
                _readyAt = Time.realtimeSinceStartup;
            }

            if (!EpilogueStartRules.StartsNow(FactsOf()))
            {
                return;
            }

            _started = true;
            Start();
        }

        /// Sets the flags and runs the step, if it may.
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

        private EpilogueStartFacts FactsOf()
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

            float readyFor = 0f;
            if (_readyAt >= 0f)
            {
                readyFor = Time.realtimeSinceStartup - _readyAt;
            }

            return new EpilogueStartFacts
            {
                AtEpilogue = atEpilogue,
                Joiner = session.IsClient,
                InWorld = WorldShare.WorldReady,
                Dreaming = dreaming,
                InEpilogue = inEpilogue,
                Started = _started,
                ReadyFor = new Seconds(readyFor),
            };
        }
    }
}
