using System;
using System.Collections;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using DarkwoodMP.World.Scripted;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.World
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
    internal sealed class Chapters
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
                    OwnPart = ScriptedEvents.Current
                        == EventPart.PlayerAndShared,
                    AllParts = ScriptedEvents.Current == EventPart.All,
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

        private Seconds _lastRequest =
            new Seconds(StoryRequestRules.NeverAsked);

        /// <summary>
        /// Joiner: our player's event reached a story step; the host decides.
        /// </summary>
        private void SendStoryRequest(
            GameEvent e, GameObject thisGO, StoryKind kind)
        {
            var now = new Seconds(Time.realtimeSinceStartup);
            if (!StoryRequestRules.MaySend(now, _lastRequest))
            {
                return; // one event can hold the step more than once
            }

            _lastRequest = now;
            GameEvents ge = null;
            if (thisGO != null)
            {
                ge = thisGO.GetComponent<GameEvents>();
            }

            ObjRef r = default;
            if (ge != null)
            {
                r = ObjRef.Of(thisGO);
            }

            // the host checks our reach (a stage starts as we arrive): our
            // newest position first
            Sync.Players.PlayerSync instance = Plugin.Instance.Host.Players;
            if (instance != null)
            {
                instance.FlushNow();
            }

            var w = _coop.Net.Begin(MsgType.StoryRequest);
            int index = 0;
            if (ge != null)
            {
                index = Math.Max(0, ge.events.IndexOf(e));
            }

            int chapter = 0;
            if (kind == StoryKind.Chapter)
            {
                chapter = e.intValue;
            }

            StoryRequest.Write(w, new StoryAsk
            {
                Event = ObjRef.NameOf(r),
                Index = (byte)index,
                Kind = kind,
                Chapter = chapter,
            });

            string what = What(kind, e.intValue);
            _coop.Authority.Request(w, what, (ok, reason, result) =>
            {
                string notice = StoryRequestRules.NoticeFor(new ReplyFacts
                {
                    Ok = ok,
                    Reason = reason,
                    Result = result,
                    What = what,
                });

                if (notice != null)
                {
                    Plugin.Notice(notice);
                }
            }, inventory: false);
        }

        /// <summary>
        /// Host: a joiner's event reached a story step. Check it, then run it
        /// here. When the host can't run it just now (it is in a dream of its
        /// own, its world isn't ready, or its game is already on its way to
        /// another part of the story), the request is held and run once the
        /// host can (Update): the joiner's trigger has fired for good in every
        /// game, so a "no" then would lose the chapter for everyone. Only a
        /// request that doesn't fit the story (the wrong chapter, an event that
        /// doesn't do that, too far away) is refused.
        /// </summary>
        private void OnStoryRequest(ulong from, PacketReader r)
        {
            var s = _coop.Net;
            if (s == null || !s.IsHost)
            {
                return;
            }

            StoryAsk ask = StoryRequest.Read(r);
            if (!_coop.Authority.Handling)
            {
                return; // only as a Request
            }

            var h = new Held
            {
                From = from,
                Obj = ObjRef.RefOf(ask.Event),
                Index = ask.Index,
                Kind = ask.Kind,
                Chapter = ask.Chapter,
                WasAway = AwayNow(from)
            };

            StoryAnswer answer = Decide(h);
            string what = What(h.Kind, h.Chapter);
            switch (answer.Outcome)
            {
                case StoryOutcome.Refuse:
                    _coop.Authority.Refuse(answer.Verdict);
                    return;
                case StoryOutcome.Done:
                    _coop.Authority.Result(
                        StoryRequestRules.DonePrefix + answer.Reason);
                    return;
                case StoryOutcome.Wait:
                    // A repeat of the one held keeps it (and when it was first
                    // checked).
                    if (!HeldRules.Repeats(StoryOf(_held), StoryOf(h)))
                    {
                        Plugin.Log.LogInfo(
                            $"{s.NameOf(from)} set off {what}, which can't "
                                + $"run here yet ({answer.Reason}): holding "
                                + "it until it can");
                        _held = h;
                        _ready = false;
                    }

                    _coop.Authority.Result(
                        StoryRequestRules.PendingPrefix + answer.Reason);
                    return;
            }

            Plugin.Log.LogInfo(
                $"{s.NameOf(from)} set off {what}: running it here");
            if (!Run(h))
            {
                _coop.Authority.Refuse(
                    Verdict.Refuse(Refusal.RunFailed));
            }
        }

        /// <summary>
        /// Host: a joiner's story step that waits until the host's game can run
        /// it.
        /// </summary>
        private sealed class Held
        {
            public ulong From;
            public ObjRef Obj;
            public int Index, Chapter;
            public StoryKind Kind;
            public bool WasAway, ReachChecked;
            public string Key => StoryRequestRules.KeyOf(Kind, Chapter);
        }

        private Held _held;
        /// <summary>
        /// Host: the held step could run on every frame since _readySince.
        /// </summary>
        private bool _ready;
        private Seconds _readySince;
        /// <summary>
        /// Host: the story steps run for a joiner or started by our own game in
        /// this session (StoryRequestRules.KeyOf): none runs twice.
        /// </summary>
        private readonly System.Collections.Generic.HashSet<string>
            _ran = new System.Collections.Generic.HashSet<string>();

        /// <summary>Who set off a held step, and which (none: zero).</summary>
        private static HeldStory StoryOf(Held h)
        {
            if (h == null)
            {
                return default;
            }

            return new HeldStory
            {
                From = h.From,
                Kind = h.Kind,
                Chapter = h.Chapter,
            };
        }

        /// <summary>
        /// Whether a joiner is away in a place of their own now (false with no
        /// puppet for them).
        /// </summary>
        private static bool AwayNow(ulong player)
        {
            Sync.Players.RemotePlayer rp = null;
            if (Plugin.Instance.Host.Players != null
                && Plugin.Instance.Host.Players.Remotes.TryGetValue(
                    player, out var remote))
            {
                rp = remote;
            }

            return rp != null && rp.Away;
        }

        /// <summary>
        /// Host: the only code reading the game for a joiner's story step. The
        /// event is looked up only once the rules get as far as it.
        /// </summary>
        private StoryFacts FactsOf(Held h)
        {
            var facts = new StoryFacts
            {
                Kind = h.Kind,
                Chapter = h.Chapter,
                Ran = _ran.Contains(h.Key),
                OnTheWay = _chapterSent || Generating || _endSent,
                HostInWorld = WorldShare.InWorld && Player.Instance != null
                    && !WorldShare.Loading && WorldShare.WorldReady,
                Dreams = Prologue.DreamsOf(Singleton<Dreams>.Instance),
                HostChapter = HostChapter,
                ReachChecked = h.ReachChecked,
                WasAway = h.WasAway,
                IsAway = AwayNow(h.From),
            };

            if (!StoryRequestRules.ReachesEvent(facts))
            {
                return facts;
            }

            GameEvents ge = null;
            if (h.Obj.Valid)
            {
                ge = ObjRef.Resolve<GameEvents>(h.Obj);
            }

            if (ge == null)
            {
                return facts;
            }

            StoryStepFacts step = default;
            if (h.Index < ge.events.Count)
            {
                step = StepOf(ge.events[h.Index]);
            }

            ReachFacts reach = _coop.Authority.NearOf(
                h.From, ge.transform.position);
            return facts with
            {
                EventFound = true,
                EventName = ge.name,
                Step = step,
                Known = reach.Known,
                StandIn = reach.StandIn,
                EventAt = reach.Target,
            };
        }

        /// <summary>
        /// Host: what to do with a joiner's story step now. Its reach is
        /// checked once, from where they were then: a held request isn't
        /// refused because they moved on meanwhile.
        /// </summary>
        private StoryAnswer Decide(Held h)
        {
            StoryAnswer answer = StoryRequestRules.Decide(FactsOf(h));
            h.ReachChecked = answer.ReachChecked;
            return answer;
        }

        /// <summary>Host: run a joiner's story step in our game.</summary>
        private bool Run(Held h)
        {
            _ran.Add(h.Key);
            try
            {
                Singleton<Controller>.Instance.StartCoroutine(
                    StepFor(h.Kind, h.Chapter).fire(
                        Player.Instance.gameObject));
                return true;
            }
            catch (Exception e)
            {
                _ran.Remove(h.Key);
                Plugin.Log.LogWarning(
                    $"Couldn't run the story step: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Host: run the held story step once our game can, and has for 3
        /// seconds (a dream's end settles).
        /// </summary>
        private void UpdateHeld()
        {
            var h = _held;
            if (h == null)
            {
                return;
            }

            var s = _coop.Net;
            bool here = s != null && s.IsHost && s.Peers.Contains(h.From);
            StoryAnswer answer = default;
            if (here)
            {
                answer = Decide(h);
            }

            HeldStep step = HeldRules.Next(new HeldFacts
            {
                PlayerHere = here,
                Outcome = answer.Outcome,
                Ready = _ready,
                ReadySince = _readySince,
                Now = new Seconds(Time.realtimeSinceStartup),
            });

            string what = What(h.Kind, h.Chapter);
            switch (step.Action)
            {
                case HeldAction.Dropped:
                    Plugin.Log.LogInfo(
                        $"Dropped the held {what}: its player left");
                    _held = null;
                    return;
                case HeldAction.Keep:
                    _ready = step.Ready;
                    _readySince = step.ReadySince;
                    return;
            }

            _held = null;
            _ready = false;
            switch (step.Action)
            {
                case HeldAction.Done:
                    Plugin.Log.LogInfo($"The held {what}: {answer.Reason}");
                    return;
                case HeldAction.Refused:
                    Plugin.Log.LogWarning(
                        $"Not running the held {what} after all: "
                            + answer.Words);
                    return;
            }

            Plugin.Log.LogInfo(
                $"{s.NameOf(h.From)} set off {what} earlier: running it here "
                    + "now");
            Run(h);
        }

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
                EpilogueSync.HostStarts();
                return;
            }

            Plugin.Log.LogInfo(
                $"The game's end ({kind}): the players in your world see it "
                    + "too, and the server stops");
            var w = s.Begin(MsgType.GameEnd);
            // phase 1: the session ends
            GameEnd.Write(w, new EndWord { Kind = kind, Phase = 1 });
            WorldShare instance = WorldShare.Instance;
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
                EpilogueSync.OnGameEnd((byte)kind, phase);
                return;
            }

            SoloEnding(kind);
        }

        /// <summary>
        /// Joiner: leave the session and see the ending alone in our copy of
        /// the host's world.
        /// </summary>
        internal static void SoloEnding(EndKind kind)
        {
            var s = S;
            if (s != null)
            {
                INetBackend backend = s.Backend;
                if (backend != null)
                {
                    backend.Leave();
                }
            }

            WorldShare instance = WorldShare.Instance;
            if (instance != null)
            {
                instance.SetLoadStatus("The host reached the end of the game.");
            }

            if (!EndingRules.ShowsEnding(
                WorldShare.InWorld, SaveRedirect.Active))
            {
                return;
            }

            Plugin.Notice("The host has reached the end of the game");
            Plugin.Instance.StartCoroutine(ShowEnding(kind));
        }

        /// <summary>
        /// Joiner, out of the session: the ending in our copy of the host's
        /// world. Saves still go to the mod's cache (SaveRedirect lasts until
        /// the main menu).
        /// </summary>
        private static IEnumerator ShowEnding(EndKind kind)
        {
            yield return new WaitForSecondsRealtime(2f); // the notice
            if (kind == EndKind.Epilogue)
            {
                bool started = false;
                try
                {
                    started = StartEpilogue();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't start the epilogue: {e.Message}");
                }

                if (started)
                {
                    float until = Time.realtimeSinceStartup
                        + EndingRules.AloneEnterFor;
                    while (Time.realtimeSinceStartup < until
                        && !(Player.Instance != null
                            && Player.Instance.inEpilogue))
                    {
                        yield return null;
                    }
                }

                if (started && Player.Instance != null
                    && Player.Instance.inEpilogue)
                {
                    Plugin.Log.LogInfo(
                        "Playing the epilogue (the credits follow, then the "
                            + "main menu)");
                    if (!Plugin.Instance.Host.TestQuickEnd)
                    {
                        yield return ToMenuAfterEnding();
                        yield break;
                    }

                    yield return new WaitForSecondsRealtime(5f);
                    Plugin.Log.LogInfo(
                        "Test: skipping the rest of the epilogue");
                }
                else
                {
                    Plugin.Log.LogInfo(
                        "The epilogue didn't start here: showing the end "
                            + "screen");
                    yield return EndScreen();
                }
            }
            else
            {
                yield return EndScreen();
            }

            ToCredits();
            yield return ToMenuAfterEnding();
        }

        /// <summary>
        /// As the epilogue's startDream step does it: inputs off, a black
        /// screen, then the epilogue dream.
        /// </summary>
        private static bool StartEpilogue()
        {
            var dreams = Singleton<Dreams>.Instance;
            var p = Player.Instance;
            var o = Singleton<OutsideLocations>.Instance;
            var facts = new StartFacts
            {
                Dreams = Prologue.DreamsOf(dreams),
                HasPlayer = p != null,
                InOutside = o != null && o.playerInOutsideLocation,
            };

            if (!EndingRules.MayStartAlone(facts))
            {
                return false;
            }

            Core.forbidInputs = true;
            p.halt();
            dreams.wantToDream = true;
            Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 2f);
            Plugin.Instance.StartCoroutine(PrepareSoon(dreams));
            return true;
        }

        private static IEnumerator PrepareSoon(Dreams dreams)
        {
            yield return new WaitForSecondsRealtime(2.1f);
            dreams.StartCoroutine(dreams.prepareDream(EpilogueDream));
        }

        private static IEnumerator EndScreen()
        {
            try
            {
                Singleton<UI>.Instance.showEndGame();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show the end screen: {e.Message}");
            }

            Plugin.Log.LogInfo("Showing the end screen");
            float time = 12f;
            if (Plugin.Instance.Host.TestQuickEnd)
            {
                time = 4f;
            }

            yield return new WaitForSecondsRealtime(time);
        }

        /// <summary>
        /// As EpilogueOutcomes.goToCredits does: fade out, then the credits
        /// scene, which ends at the main menu.
        /// </summary>
        internal static void ToCredits()
        {
            Plugin.Log.LogInfo("To the credits");
            try
            {
                Core.unpause();
                Singleton<Controller>.Instance.fadeAudio(
                    fadeOut: true, 3f, musicToo: true);
                Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 3f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't fade out for the credits: {e.Message}");
            }

            Core.forbidInputs = true;
            Core.coreStarted = false;
            Core.loadingGame = false;
            Core.loadedGame = false;
            Plugin.Instance.StartCoroutine(CreditsSoon());
        }

        private static IEnumerator CreditsSoon()
        {
            yield return new WaitForSecondsRealtime(3.5f);
            Core.cantChangeForbidInputs = false;
            SceneManager.LoadScene("credits");
        }

        internal static IEnumerator ToMenuAfterEnding()
        {
            bool credits = false;
            while (!WorldShare.MainMenuReady())
            {
                if (!credits && SceneManager.GetActiveScene().name == "credits")
                {
                    credits = true;
                    Plugin.Log.LogInfo("The credits are rolling");
                    if (Plugin.Instance.Host.TestQuickEnd)
                    {
                        Plugin.Instance.StartCoroutine(SkipCredits());
                    }
                }

                yield return null;
            }

            Plugin.Log.LogInfo("Back at the main menu after the ending");
            Plugin.Instance.OpenPanel(); // it says why (LoadStatus)
        }

        private static IEnumerator SkipCredits()
        {
            yield return new WaitForSecondsRealtime(3f);
            var c = UnityEngine.Object.FindObjectOfType<Credits>();
            if (c != null)
            {
                AccessTools.Method(typeof(Credits), "exitToMainMenu").Invoke(
                    c, null);
            }
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.generateChapter))]
    static class GenerateChapterPatch
    {
        static bool Prefix(int _chapterId, bool generateSave)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Chapters.BeforeGenerate(_chapterId, generateSave);
        }
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.prepareDream))]
    static class EpilogueStartPatch
    {
        static void Prefix(string presetName)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Chapters.OnPrepareDream(presetName);
            }
        }
    }

    [HarmonyPatch(typeof(UI), nameof(UI.showEndGame))]
    static class EndScreenPatch
    {
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Chapters.OnShowEndGame();
            }
        }
    }
}
