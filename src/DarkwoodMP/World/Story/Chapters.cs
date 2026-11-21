using System;
using System.Collections;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The story's turning points: a new chapter and the game's end. Both are
    /// the host's: they change (or end) the world everyone is in, so a joiner's
    /// game never runs them itself.
    ///
    /// A chapter starts with a GameEvent step (transportPlayerToObject with
    /// activeModifier: save, then Controller.generateChapter). The game's end
    /// is the epilogue (a startDream step into "epilog_part1a_dream"), or the
    /// older end screen (an endGame step, UI.showEndGame). These "story steps"
    /// are skipped in a joiner's game whatever set them off. When a joiner's
    /// own event reaches one, the joiner asks the host (StoryRequest, as a
    /// Request): the host checks it's the next chapter (or the last chapter,
    /// for the end), that the step is in that event when the event is in the
    /// host's world too, and then that the player is near it, or else (the
    /// event is in a separate location of the joiner's own, which the host
    /// hasn't loaded) that the player is away in one. Then the host runs the
    /// step in its own game, or, when it can't just then (in a dream of its
    /// own, its world not ready), holds it and runs it once it can, since the
    /// joiner's trigger has already fired for good. On the host, a story step
    /// runs only for its own events and the world's, not as the host's part of
    /// a joiner's EventFired (the StoryRequest is that).
    ///
    /// A chapter on the host: ChapterStart goes to the joiners as the step
    /// starts (they send their characters and wait under a black screen), the
    /// host's game saves and builds the chapter, and once its new world is
    /// ready WorldShare sends WorldReload, so the joiners download it and load
    /// it. Their characters come back as the game carries the host's own over:
    /// the same level, skills and items (the chapter's
    /// ChapterPreset.initInventory and initPlayer only apply to a chapter
    /// started without a save to carry over), standing beside the host
    /// (Characters, CharacterLoad kind 3).
    ///
    /// The end on the host: the epilogue is played together (EpilogueSync:
    /// GameEnd phase 0 and the session stays up; phase 1 at the credits stops
    /// the server). The older end screen sends GameEnd phase 1 and stops the
    /// server at once; each joiner leaves, sees the end screen, and the credits
    /// take it to the main menu. A joiner whose epilogue can't start falls back
    /// to that too.
    /// </summary>
    internal sealed partial class Chapters
    {
        private static Session S => Session.Instance;

        private readonly CoopSession _coop;

        public const string EpilogueDream = StoryRequestRules.EpilogueDream;
        /// <summary>
        /// The game's last chapter: the epilogue comes after it.
        /// </summary>
        public const int LastChapter = StoryRequestRules.LastChapter;

        /// <summary>
        /// Host: its next chapter is being built (from generateChapter until
        /// its world is ready again).
        /// </summary>
        public bool Generating { get; private set; }
        private bool _chapterSent, _endSent;

        /// <summary>The chapter of the world loaded now (0 if none).</summary>
        public static int HostChapter
        {
            get
            {
                if (Singleton<WorldGenerator>.Instance != null)
                {
                    return Singleton<WorldGenerator>.Instance.chapterID;
                }

                return 0;
            }
        }

        public Chapters(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.ChapterStart, OnChapterStart);
            coop.Handle(MsgType.GameEnd, OnGameEnd);
            coop.Handle(MsgType.StoryRequest, OnStoryRequest);
        }

        public void Update()
        {
            if (ChapterRules.ChapterReady(Generating, WorldShare.WorldReady))
            {
                Generating = false;
                _chapterSent = false;
                Plugin.Log.LogInfo($"Chapter {HostChapter} is ready");
            }

            UpdateHeld();
        }

        /// <summary>A step that starts a chapter or ends the game.</summary>
        public static bool IsStoryStep(GameEvent e) =>
            KindOf(e) != StoryKind.None;

        internal static StoryKind KindOf(GameEvent e) =>
            StoryRequestRules.KindOf(StepOf(e));

        /// <summary>
        /// The only code reading a GameEvent step for the rules (the zero
        /// facts for none).
        /// </summary>
        private static StoryStepFacts StepOf(GameEvent e)
        {
            if (e == null)
            {
                return default;
            }

            var action = StepAction.Other;
            if (e.type == GameEvent.Type.transportPlayerToObject)
            {
                action = StepAction.Transport;
            }
            else if (e.type == GameEvent.Type.startDream)
            {
                action = StepAction.StartDream;
            }
            else if (e.type == GameEvent.Type.endGame)
            {
                action = StepAction.EndGame;
            }

            return new StoryStepFacts
            {
                Action = action,
                ActiveModifier = e.activeModifier,
                Value = e.Value,
                Chapter = e.intValue,
            };
        }

        /// <summary>
        /// The step as the host runs it for a joiner (the event itself may be
        /// in a place only the joiner has).
        /// </summary>
        private static GameEvent StepFor(StoryKind kind, int chapter)
        {
            switch (kind)
            {
                case StoryKind.Chapter:
                    return new GameEvent
                    {
                        type = GameEvent.Type.transportPlayerToObject,
                        activeModifier = true,
                        intValue = chapter,
                        eventAmount = 1
                    };

                case StoryKind.Epilogue:
                    return new GameEvent
                    {
                        type = GameEvent.Type.startDream,
                        Value = EpilogueDream,
                        eventAmount = 1
                    };

                default:
                    return new GameEvent
                    {
                        type = GameEvent.Type.endGame,
                        eventAmount = 1
                    };
            }
        }

        /// <summary>
        /// Whether a story step runs here now: never in a joiner's game, and on
        /// the host for its own events and a joiner's (the host's part), not
        /// for one another game ran first.
        /// </summary>
        public static bool StoryStepRuns(GameEvent e, GameObject thisGO)
        {
            var s = S;
            var facts = new StepRunFacts();
            if (s != null && s.Active)
            {
                facts = new StepRunFacts
                {
                    InSession = true,
                    IsClient = s.IsClient,
                    OwnPart = Plugin.Instance.Host.Parts.Current
                        == EventPart.PlayerAndShared,
                    AllParts = Plugin.Instance.Host.Parts.Current
                        == EventPart.All,
                    Kind = KindOf(e),
                };
            }

            StepRun run = StoryRequestRules.StepRuns(facts);
            CoopSession coop = Coop.Current;
            if (facts.IsClient)
            {
                string text = "";
                if (facts.Kind == StoryKind.Chapter)
                {
                    text = $" {e.intValue}";
                }

                string action = "not run here";
                if (run == StepRun.AskHost)
                {
                    action = "asking the host";
                }

                Plugin.Log.LogInfo(
                    $"A story step ({facts.Kind}{text}) is the host's: "
                        + action);
            }

            switch (run)
            {
                case StepRun.AskHost:
                    if (coop != null)
                    {
                        coop.Chapters.SendStoryRequest(e, thisGO, facts.Kind);
                    }

                    return false;
                case StepRun.Skip:
                    return false;
                case StepRun.StartChapter:
                    if (coop != null)
                    {
                        coop.Chapters.SendChapterStart(e.intValue);
                    }

                    return true;
                default:
                    return true;
            }
        }

        private static string What(StoryKind kind, int chapter) =>
            StoryRequestRules.What(kind, chapter);

        // ---- host -----------------------------------------------------------

        private void SendChapterStart(int chapter)
        {
            var s = _coop.Net;
            if (!ChapterRules.SendsChapterStart(
                s != null && s.IsHost, PeersOf(s), _chapterSent))
            {
                return;
            }

            _chapterSent = true;
            _ran.Add(StoryRequestRules.KeyOf(StoryKind.Chapter, chapter));
            Plugin.Log.LogInfo(
                $"Starting chapter {chapter}: the players in your world follow "
                    + "once it's ready");
            var w = s.Begin(MsgType.ChapterStart);
            ChapterStart.Write(w, chapter);
            s.Broadcast(w, Channel.Control, reliable: true);
        }

        private static int PeersOf(Session s)
        {
            if (s == null)
            {
                return 0;
            }

            return s.Peers.Count;
        }

        /// <summary>
        /// Controller.generateChapter: a joiner's game doesn't build a chapter
        /// itself; the host tells the joiners.
        /// </summary>
        internal bool BeforeGenerate(int chapter, bool generateSave)
        {
            var s = _coop.Net;
            var facts = new GenerateFacts { NewChapter = generateSave };
            if (s != null && s.Active)
            {
                facts = facts with
                {
                    InSession = true,
                    IsClient = s.IsClient,
                };
            }

            GenerateStep step = ChapterRules.BeforeGenerate(facts);
            if (step == GenerateStep.Run)
            {
                // loading a chapter's save (also a joiner loading the host's)
                return true;
            }

            if (step == GenerateStep.Skip)
            {
                Plugin.Log.LogWarning(
                    $"Not starting chapter {chapter} here: the host's game "
                        + "starts chapters");
                return false;
            }

            SendChapterStart(chapter);
            Generating = true;
            return true;
        }

        internal void HostEnding(EndKind kind)
        {
            var s = _coop.Net;
            kind = EndingRules.HostEnds(s != null && s.IsHost, _endSent, kind);
            if (kind == EndKind.None)
            {
                return;
            }

            _endSent = true;
            _ran.Add(StoryRequestRules.KeyOf(StoryKind.Epilogue, 0));
            if (kind == EndKind.Epilogue)
            {
                _coop.Epilogue.HostStarts();
                return;
            }

            Plugin.Log.LogInfo(
                $"The game's end ({kind}): the players in your world see it "
                    + "too, and the server stops");
            var w = s.Begin(MsgType.GameEnd);
            // phase 1: the session ends
            GameEnd.Write(w, new EndWord { Kind = kind, Phase = 1 });
            WorldShare instance = Plugin.Instance.Host.Share;
            if (instance != null)
            {
                instance.StopServer(w);
            }
        }

        internal void OnPrepareDream(string preset)
        {
            if (EndingRules.DreamEnds(preset))
            {
                HostEnding(EndKind.Epilogue);
            }
        }

        internal void OnShowEndGame() => HostEnding(EndKind.EndScreen);

        // ---- joiner ---------------------------------------------------------

        private Seconds _chapterWaitUntil;

        private void OnChapterStart(ulong from, PacketReader r)
        {
            var s = _coop.Net;
            if (s == null || s.IsHost || from != s.HostId)
            {
                return;
            }

            int chapter = ChapterStart.Read(r);
            // before the host's game saves and moves on
            Characters instance = Characters.Instance;
            if (instance != null)
            {
                instance.SaveNow();
            }

            Plugin.Notice(
                $"The host is starting chapter {chapter}. You'll follow once "
                    + "it's ready.");
            if (!WorldShare.InWorld)
            {
                return;
            }

            try
            {
                Singleton<UI>.Instance.tweenBlackScreen(Color.black, 1f);
                Player.Instance.halt();
                Core.forbidInputs = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't fade out for the chapter: {e.Message}");
            }

            ChapterWait wait = ChapterRules.Extend(
                _chapterWaitUntil, new Seconds(Time.realtimeSinceStartup));
            _chapterWaitUntil = wait.Until;
            if (wait.Starts)
            {
                Plugin.Instance.StartCoroutine(
                    ChapterWait(SceneManager.GetActiveScene().handle));
            }
        }

        /// <summary>
        /// If the host's WorldReload never comes (its chapter failed), let our
        /// player go on after a while.
        /// </summary>
        private IEnumerator ChapterWait(int scene)
        {
            while (true)
            {
                ChapterWaitStep step = ChapterRules.Wait(new ChapterWaitFacts
                {
                    Now = new Seconds(Time.realtimeSinceStartup),
                    Until = _chapterWaitUntil,
                    SceneChanged =
                        SceneManager.GetActiveScene().handle != scene,
                    InWorld = WorldShare.InWorld,
                });

                if (step == ChapterWaitStep.TimedOut)
                {
                    break;
                }

                if (step == ChapterWaitStep.Over)
                {
                    _chapterWaitUntil = default;
                    yield break;
                }

                yield return null;
            }

            Plugin.Notice("The host's next chapter didn't come");
            Core.forbidInputs = false;
            Singleton<UI>.Instance.tweenBlackScreen(
                new Color(0f, 0f, 0f, 0f), 1f);
        }

        private void OnGameEnd(ulong from, PacketReader r)
        {
            var s = _coop.Net;
            bool fromHost = s != null && !s.IsHost && from == s.HostId;
            if (!fromHost)
            {
                return;
            }

            EndWord word = GameEnd.Read(r);
            var kind = word.Kind;
            byte phase = word.Phase;
            // The epilogue's steps are EpilogueSync's: only whether it's
            // one of them is read here.
            GameEndStep step = EndingRules.GameEnd(new GameEndFacts
            {
                FromHost = fromHost,
                Kind = (byte)kind,
                Phase = phase,
            });

            Plugin.Log.LogInfo(
                $"The host reached the game's end ({kind}, phase {phase})");
            if (step != GameEndStep.Solo)
            {
                _coop.Epilogue.OnGameEnd((byte)kind, phase);
                return;
            }

            SoloEnding(kind);
        }
    }
}
