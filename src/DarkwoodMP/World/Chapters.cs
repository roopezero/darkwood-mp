using System;
using System.Collections;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public static class Chapters
    {
        private static Session S => Session.Instance;

        public const string EpilogueDream = "epilog_part1a_dream";
        /// <summary>
        /// The game's last chapter: the epilogue comes after it.
        /// </summary>
        public const int LastChapter = 2;
        internal enum EndKind : byte
        {
            Epilogue = 1,
            EndScreen = 2
        }
        internal enum StoryKind : byte
        {
            None = 0,
            Chapter = 1,
            Epilogue = 2,
            EndScreen = 3
        }

        /// <summary>
        /// Host: its next chapter is being built (from generateChapter until
        /// its world is ready again).
        /// </summary>
        public static bool Generating { get; private set; }
        private static bool _chapterSent, _endSent;

        /// <summary>
        /// For tests: skip the epilogue and the credits as soon as they start.
        /// </summary>
        public static bool TestQuick;

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

        public static void Register(Session s)
        {
            s.Register(MsgType.ChapterStart, OnChapterStart);
            s.Register(MsgType.GameEnd, OnGameEnd);
            s.Register(MsgType.StoryRequest, OnStoryRequest);
            s.OnSessionEnded += () =>
            {
                _chapterSent = false;
                _endSent = false;
                Generating = false;
                _held = null;
                _readySince = -1f;
                _ran.Clear();
            };
        }

        public static void Update()
        {
            if (Generating && WorldShare.WorldReady)
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

        internal static StoryKind KindOf(GameEvent e)
        {
            if (e == null)
            {
                return StoryKind.None;
            }
            if (e.type == GameEvent.Type.transportPlayerToObject
                && e.activeModifier)
            {
                return StoryKind.Chapter;
            }
            if (e.type == GameEvent.Type.startDream && e.Value == EpilogueDream)
            {
                return StoryKind.Epilogue;
            }
            if (e.type == GameEvent.Type.endGame)
            {
                return StoryKind.EndScreen;
            }
            return StoryKind.None;
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
            if (s == null || !s.Active)
            {
                return true;
            }
            var kind = KindOf(e);
            if (s.IsClient)
            {
                bool ours = ScriptedEvents.Current
                    == ScriptedEvents.Part.PlayerAndShared;
                string text = "";
                if (kind == StoryKind.Chapter)
                {
                    text = $" {e.intValue}";
                }
                string action = "not run here";
                if (ours)
                {
                    action = "asking the host";
                }
                Plugin.Log.LogInfo(
                    $"A story step ({kind}{text}) is the host's: " + action);
                if (ours)
                {
                    SendStoryRequest(e, thisGO, kind);
                }
                return false;
            }
            if (ScriptedEvents.Current != ScriptedEvents.Part.All)
            {
                return false;
            }
            if (kind == StoryKind.Chapter)
            {
                SendChapterStart(e.intValue);
            }
            return true;
        }

        private static string What(StoryKind kind, int chapter)
        {
            if (kind == StoryKind.Chapter)
            {
                return $"chapter {chapter}";
            }
            return $"the game's end ({kind})";
        }

        private static float _lastRequest = -999f;

        /// <summary>
        /// Joiner: our player's event reached a story step; the host decides.
        /// </summary>
        private static void SendStoryRequest(
            GameEvent e, GameObject thisGO, StoryKind kind)
        {
            if (Time.realtimeSinceStartup - _lastRequest < 5f)
            {
                return; // one event can hold the step more than once
            }
            _lastRequest = Time.realtimeSinceStartup;
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
            Sync.PlayerSync instance = Sync.PlayerSync.Instance;
            if (instance != null)
            {
                instance.FlushNow();
            }
            var w = S.Begin(MsgType.StoryRequest);
            r.Write(w);
            int index = 0;
            if (ge != null)
            {
                index = Math.Max(0, ge.events.IndexOf(e));
            }
            w.W.Write((byte)index);
            w.W.Write((byte)kind);
            int chapter = 0;
            if (kind == StoryKind.Chapter)
            {
                chapter = e.intValue;
            }
            w.W.Write(chapter);
            string what = What(kind, e.intValue);
            Authority.Request(w, what, (ok, reason, result) =>
            {
                if (!ok)
                {
                    Plugin.Notice($"The host didn't start {what}: {reason}");
                }
                else if (result != null && result.StartsWith("pending: "))
                {
                    Plugin.Notice(
                        $"The host will start {what} as soon as it can ({result.Substring(9)})");
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
        private static void OnStoryRequest(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsHost)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            int index = r.R.ReadByte();
            var kind = (StoryKind)r.R.ReadByte();
            int chapter = r.R.ReadInt32();
            if (!Authority.Handling)
            {
                return; // only as a Request
            }
            Sync.RemotePlayer rp = null;
            if (Sync.PlayerSync.Instance != null
                && Sync.PlayerSync.Instance.Remotes.TryGetValue(
                    from, out var remote))
            {
                rp = remote;
            }
            var h = new Held
            {
                From = from,
                Obj = obj,
                Index = index,
                Kind = kind,
                Chapter = chapter,
                WasAway = rp != null && rp.Away
            };
            string reason;
            switch (Decide(h, out reason))
            {
                case Verdict.Refuse:
                    Authority.Refuse(reason);
                    return;
                case Verdict.Done:
                    Authority.Result("done: " + reason);
                    return;
                case Verdict.Wait:
                    // A repeat of the one held keeps it (and when it was first
                    // checked).
                    if (_held == null || _held.Key != h.Key
                        || _held.From != from)
                    {
                        Plugin.Log.LogInfo(
                            $"{s.NameOf(from)} set off {What(kind, chapter)}, which can't run here yet ({reason}): holding it until it can");
                        _held = h;
                        _readySince = -1f;
                    }
                    Authority.Result("pending: " + reason);
                    return;
            }
            Plugin.Log.LogInfo(
                $"{s.NameOf(from)} set off {What(kind, chapter)}: running it here");
            if (!Run(h))
            {
                Authority.Refuse("it couldn't run");
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
            public string Key => KeyOf(Kind, Chapter);
        }
        private static Held _held;
        private static float _readySince = -1f;
        /// <summary>
        /// Host: the story steps run for a joiner or started by our own game in
        /// this session (KeyOf): none runs twice.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> _ran =
            new System.Collections.Generic.HashSet<string>();
        private static string KeyOf(StoryKind kind, int chapter)
        {
            if (kind == StoryKind.Chapter)
            {
                return "chapter " + chapter;
            }
            return "end";
        }

        private enum Verdict
        {
            Run,
            Done,
            Wait,
            Refuse
        }

        /// <summary>
        /// Whether the host's player is in a dream, or on the way into or out
        /// of one.
        /// </summary>
        private static bool HostDreaming()
        {
            var d = Singleton<Dreams>.Instance;
            return Core.EnteringDream
                || (d != null
                    && (d.dreaming || d.dreamPrepared || d.switchingDream
                        || (d.currentTransition != null
                            && d.currentTransition.isPlaying)));
        }

        /// <summary>
        /// Host: what to do with a joiner's story step now. reason: why it
        /// waits, or why it's refused.
        /// </summary>
        private static Verdict Decide(Held h, out string reason)
        {
            reason = null;
            var kind = h.Kind;
            if (kind != StoryKind.Chapter && kind != StoryKind.Epilogue
                && kind != StoryKind.EndScreen)
            {
                reason = "no such story step";
                return Verdict.Refuse;
            }
            if (_ran.Contains(h.Key))
            {
                reason = "the host's game has already started it";
                return Verdict.Done;
            }
            if (_chapterSent || Generating || _endSent)
            {
                reason =
                    "the host's game is on its way to another part of the "
                        + "story";
                return Verdict.Wait;
            }
            if (!WorldShare.InWorld || Player.Instance == null
                || WorldShare.Loading || !WorldShare.WorldReady)
            {
                reason = "the host isn't in its world yet";
                return Verdict.Wait;
            }
            if (HostDreaming())
            {
                reason = "the host is in a dream";
                return Verdict.Wait;
            }
            int now = HostChapter;
            if (kind == StoryKind.Chapter && h.Chapter != now + 1)
            {
                reason =
                    $"chapter {h.Chapter} isn't the next one (this is chapter {now})";
                return Verdict.Refuse;
            }
            if (kind == StoryKind.Epilogue && now < LastChapter)
            {
                reason =
                    $"the epilogue comes after chapter {LastChapter} (this is chapter {now})";
                return Verdict.Refuse;
            }
            GameEvents ge = null;
            if (h.Obj.Valid)
            {
                ge = ObjRef.Resolve<GameEvents>(h.Obj);
            }
            if (ge != null)
            {
                GameEvent step = null;
                if (h.Index < ge.events.Count)
                {
                    step = ge.events[h.Index];
                }
                if (KindOf(step) != kind
                    || (kind == StoryKind.Chapter
                        && step.intValue != h.Chapter))
                {
                    reason = $"{ge.name} doesn't do that";
                    return Verdict.Refuse;
                }
                // Checked once, from where they were then: a held request isn't
                // refused because they moved on meanwhile.
                if (!h.ReachChecked)
                {
                    h.ReachChecked = true;
                    if (!Authority.Near(h.From, ge.transform.position, 3000f))
                    {
                        reason = $"{ge.name} is more than 3000 away from them";
                        return Verdict.Refuse;
                    }
                }
                return Verdict.Run;
            }
            Sync.RemotePlayer rp = null;
            if (Sync.PlayerSync.Instance != null
                && Sync.PlayerSync.Instance.Remotes.TryGetValue(
                    h.From, out var remote))
            {
                rp = remote;
            }
            if (!h.WasAway && !(rp != null && rp.Away))
            {
                reason =
                    "that event isn't in the host's world, and they aren't in "
                        + "a place of their own";
                return Verdict.Refuse;
            }
            return Verdict.Run;
        }

        /// <summary>Host: run a joiner's story step in our game.</summary>
        private static bool Run(Held h)
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
        private static void UpdateHeld()
        {
            var h = _held;
            if (h == null)
            {
                return;
            }
            var s = S;
            if (s == null || !s.IsHost || !s.Peers.Contains(h.From))
            {
                Plugin.Log.LogInfo(
                    $"Dropped the held {What(h.Kind, h.Chapter)}: its player left");
                _held = null;
                return;
            }
            var v = Decide(h, out string reason);
            if (v == Verdict.Wait)
            {
                _readySince = -1f;
                return;
            }
            if (v == Verdict.Run)
            {
                if (_readySince < 0f)
                {
                    _readySince = Time.realtimeSinceStartup;
                }
                if (Time.realtimeSinceStartup - _readySince < 3f)
                {
                    return;
                }
            }
            _held = null;
            _readySince = -1f;
            if (v == Verdict.Done)
            {
                Plugin.Log.LogInfo(
                    $"The held {What(h.Kind, h.Chapter)}: {reason}");
                return;
            }
            if (v == Verdict.Refuse)
            {
                Plugin.Log.LogWarning(
                    $"Not running the held {What(h.Kind, h.Chapter)} after all: {reason}");
                return;
            }
            Plugin.Log.LogInfo(
                $"{s.NameOf(h.From)} set off {What(h.Kind, h.Chapter)} earlier: running it here now");
            Run(h);
        }

        // ---- host -----------------------------------------------------------

        private static void SendChapterStart(int chapter)
        {
            var s = S;
            if (s == null || !s.IsHost || s.Peers.Count == 0 || _chapterSent)
            {
                return;
            }
            _chapterSent = true;
            _ran.Add(KeyOf(StoryKind.Chapter, chapter));
            Plugin.Log.LogInfo(
                $"Starting chapter {chapter}: the players in your world follow once it's ready");
            var w = s.Begin(MsgType.ChapterStart);
            w.W.Write(chapter);
            s.Broadcast(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Controller.generateChapter: a joiner's game doesn't build a chapter
        /// itself; the host tells the joiners.
        /// </summary>
        internal static bool BeforeGenerate(int chapter, bool generateSave)
        {
            var s = S;
            if (s == null || !s.Active || !generateSave)
            {
                // loading a chapter's save (also a joiner loading the host's)
                return true;
            }
            if (s.IsClient)
            {
                Plugin.Log.LogWarning(
                    $"Not starting chapter {chapter} here: the host's game starts chapters");
                return false;
            }
            SendChapterStart(chapter);
            Generating = true;
            return true;
        }

        internal static void HostEnding(EndKind kind)
        {
            var s = S;
            if (s == null || !s.IsHost || _endSent)
            {
                return;
            }
            _endSent = true;
            _ran.Add(KeyOf(StoryKind.Epilogue, 0));
            if (kind == EndKind.Epilogue)
            {
                EpilogueSync.HostStarts();
                return;
            }
            Plugin.Log.LogInfo(
                $"The game's end ({kind}): the players in your world see it too, and the server stops");
            var w = s.Begin(MsgType.GameEnd);
            w.W.Write((byte)kind);
            w.W.Write((byte)1); // phase 1: the session ends
            WorldShare instance = WorldShare.Instance;
            if (instance != null)
            {
                instance.StopServer(w);
            }
        }

        internal static void OnPrepareDream(string preset)
        {
            if (preset == EpilogueDream)
            {
                HostEnding(EndKind.Epilogue);
            }
        }
        internal static void OnShowEndGame() => HostEnding(EndKind.EndScreen);

        // ---- joiner ---------------------------------------------------------

        private static float _chapterWaitUntil;

        private static void OnChapterStart(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || s.IsHost || from != s.HostId)
            {
                return;
            }
            int chapter = r.R.ReadInt32();
            // before the host's game saves and moves on
            Characters instance = Characters.Instance;
            if (instance != null)
            {
                instance.SaveNow();
            }
            Plugin.Notice(
                $"The host is starting chapter {chapter}. You'll follow once it's ready.");
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
            bool waiting = _chapterWaitUntil > Time.realtimeSinceStartup;
            _chapterWaitUntil = Time.realtimeSinceStartup + 300f;
            if (!waiting)
            {
                Plugin.Instance.StartCoroutine(
                    ChapterWait(SceneManager.GetActiveScene().handle));
            }
        }

        /// <summary>
        /// If the host's WorldReload never comes (its chapter failed), let our
        /// player go on after a while.
        /// </summary>
        private static IEnumerator ChapterWait(int scene)
        {
            while (Time.realtimeSinceStartup < _chapterWaitUntil)
            {
                if (SceneManager.GetActiveScene().handle != scene
                    || !WorldShare.InWorld)
                {
                    _chapterWaitUntil = 0f;
                    yield break;
                }
                yield return null;
            }
            Plugin.Notice("The host's next chapter didn't come");
            Core.forbidInputs = false;
            Singleton<UI>.Instance.tweenBlackScreen(
                new Color(0f, 0f, 0f, 0f), 1f);
        }

        private static void OnGameEnd(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || s.IsHost || from != s.HostId)
            {
                return;
            }
            var kind = (EndKind)r.R.ReadByte();
            byte phase = r.R.ReadByte();
            Plugin.Log.LogInfo(
                $"The host reached the game's end ({kind}, phase {phase})");
            if (EpilogueSync.OnGameEnd((byte)kind, phase))
            {
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
            if (!WorldShare.InWorld || !SaveRedirect.Active)
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
                    float until = Time.realtimeSinceStartup + 45f;
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
                    if (!TestQuick)
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
            if (dreams == null || p == null || dreams.dreaming)
            {
                return false;
            }
            if (Singleton<OutsideLocations>.Instance != null
                && Singleton<OutsideLocations>.Instance.playerInOutsideLocation)
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
            if (TestQuick)
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
                    if (TestQuick)
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
        static bool Prefix(int _chapterId, bool generateSave) =>
            Chapters.BeforeGenerate(_chapterId, generateSave);
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.prepareDream))]
    static class EpilogueStartPatch
    {
        static void Prefix(string presetName) =>
            Chapters.OnPrepareDream(presetName);
    }

    [HarmonyPatch(typeof(UI), nameof(UI.showEndGame))]
    static class EndScreenPatch
    {
        static void Prefix() => Chapters.OnShowEndGame();
    }
}
