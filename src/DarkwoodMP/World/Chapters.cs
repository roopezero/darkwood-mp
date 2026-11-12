using System;
using System.Collections;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The story's turning points: a new chapter and the game's end. Both are the host's: they change (or end) the
    /// world everyone is in, so a joiner's game never runs them itself.
    ///
    /// A chapter starts with a GameEvent step (transportPlayerToObject with activeModifier: save, then
    /// Controller.generateChapter). The game's end is the epilogue (a startDream step into "epilog_part1a_dream"),
    /// or the older end screen (an endGame step, UI.showEndGame). These "story steps" are skipped in a joiner's game
    /// whatever set them off. When a joiner's own event reaches one, the joiner asks the host (StoryRequest, as a
    /// Request): the host checks it's the next chapter (or the last chapter, for the end), that the step is in that
    /// event when the event is in the host's world too, and then that the player is near it, or else (the event is in a
    /// separate location of the joiner's own, which the host hasn't loaded) that the player is away in one. Then
    /// the host runs the step in its own game. On the host, a story step runs only for its own events and the world's,
    /// not as the host's part of a joiner's EventFired (the StoryRequest is that).
    ///
    /// A chapter on the host: ChapterStart goes to the joiners as the step starts (they send their characters and
    /// wait under a black screen), the host's game saves and builds the chapter, and once its new world is ready
    /// WorldShare sends WorldReload, so the joiners download it and load it. Their characters come back as the game
    /// carries the host's own over: the same level, skills and items (the chapter's ChapterPreset.initInventory and
    /// initPlayer only apply to a chapter started without a save to carry over), standing beside the host
    /// (Characters, CharacterLoad kind 3).
    ///
    /// The end on the host: GameEnd goes to the joiners and the server stops (the host plays its ending alone). Each
    /// joiner leaves, plays the epilogue in its own copy of the world (the flags are the host's, so the outcomes are
    /// too), or, if that can't start, sees the end screen, and then the credits take it to the main menu.
    /// </summary>
    public static class Chapters
    {
        private static Session S => Session.Instance;

        public const string EpilogueDream = "epilog_part1a_dream";
        /// <summary>The game's last chapter: the epilogue comes after it.</summary>
        public const int LastChapter = 2;
        internal enum EndKind : byte { Epilogue = 1, EndScreen = 2 }
        internal enum StoryKind : byte { None = 0, Chapter = 1, Epilogue = 2, EndScreen = 3 }

        /// <summary>Host: its next chapter is being built (from generateChapter until its world is ready again).</summary>
        public static bool Generating { get; private set; }
        private static bool _chapterSent, _endSent;

        /// <summary>For tests: skip the epilogue and the credits as soon as they start.</summary>
        public static bool TestQuick;

        /// <summary>The chapter of the world loaded now (0 if none).</summary>
        public static int HostChapter => Singleton<WorldGenerator>.Instance != null ? Singleton<WorldGenerator>.Instance.chapterID : 0;

        public static void Register(Session s)
        {
            s.Register(MsgType.ChapterStart, OnChapterStart);
            s.Register(MsgType.GameEnd, OnGameEnd);
            s.Register(MsgType.StoryRequest, OnStoryRequest);
            s.OnSessionEnded += () => { _chapterSent = false; _endSent = false; Generating = false; };
        }

        public static void Update()
        {
            if (Generating && WorldShare.WorldReady) { Generating = false; _chapterSent = false; Plugin.Log.LogInfo($"Chapter {HostChapter} is ready"); }
        }

        /// <summary>A step that starts a chapter or ends the game.</summary>
        public static bool IsStoryStep(GameEvent e) => KindOf(e) != StoryKind.None;

        internal static StoryKind KindOf(GameEvent e)
        {
            if (e == null) return StoryKind.None;
            if (e.type == GameEvent.Type.transportPlayerToObject && e.activeModifier) return StoryKind.Chapter;
            if (e.type == GameEvent.Type.startDream && e.Value == EpilogueDream) return StoryKind.Epilogue;
            if (e.type == GameEvent.Type.endGame) return StoryKind.EndScreen;
            return StoryKind.None;
        }

        /// <summary>The step as the host runs it for a joiner (the event itself may be in a place only the joiner has).</summary>
        private static GameEvent StepFor(StoryKind kind, int chapter)
        {
            switch (kind)
            {
                case StoryKind.Chapter: return new GameEvent { type = GameEvent.Type.transportPlayerToObject, activeModifier = true, intValue = chapter, eventAmount = 1 };
                case StoryKind.Epilogue: return new GameEvent { type = GameEvent.Type.startDream, Value = EpilogueDream, eventAmount = 1 };
                default: return new GameEvent { type = GameEvent.Type.endGame, eventAmount = 1 };
            }
        }

        /// <summary>
        /// Whether a story step runs here now: never in a joiner's game, and on the host for its own events and a
        /// joiner's (the host's part), not for one another game ran first.
        /// </summary>
        public static bool StoryStepRuns(GameEvent e, GameObject thisGO)
        {
            var s = S;
            if (s == null || !s.Active) return true;
            var kind = KindOf(e);
            if (s.IsClient)
            {
                bool ours = ScriptedEvents.Current == ScriptedEvents.Part.PlayerAndShared;
                Plugin.Log.LogInfo($"A story step ({kind}{(kind == StoryKind.Chapter ? $" {e.intValue}" : "")}) is the host's: " + (ours ? "asking the host" : "not run here"));
                if (ours) SendStoryRequest(e, thisGO, kind);
                return false;
            }
            if (ScriptedEvents.Current != ScriptedEvents.Part.All) return false;
            if (kind == StoryKind.Chapter) SendChapterStart(e.intValue);
            return true;
        }

        private static float _lastRequest = -999f;

        /// <summary>Joiner: our player's event reached a story step; the host decides.</summary>
        private static void SendStoryRequest(GameEvent e, GameObject thisGO, StoryKind kind)
        {
            if (Time.realtimeSinceStartup - _lastRequest < 5f) return; // one event can hold the step more than once
            _lastRequest = Time.realtimeSinceStartup;
            var ge = thisGO != null ? thisGO.GetComponent<GameEvents>() : null;
            var r = ge != null ? ObjRef.Of(thisGO) : default;
            var w = S.Begin(MsgType.StoryRequest);
            r.Write(w);
            w.W.Write((byte)(ge != null ? Math.Max(0, ge.events.IndexOf(e)) : 0));
            w.W.Write((byte)kind);
            w.W.Write(kind == StoryKind.Chapter ? e.intValue : 0);
            string what = kind == StoryKind.Chapter ? $"chapter {e.intValue}" : $"the game's end ({kind})";
            Authority.Request(w, what, (ok, reason, result) =>
            {
                if (!ok) Plugin.Notice($"The host didn't start {what}: {reason}");
            }, inventory: false);
        }

        /// <summary>Host: a joiner's event reached a story step. Check it, then run it here.</summary>
        private static void OnStoryRequest(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsHost) return;
            var obj = ObjRef.Read(r);
            int index = r.R.ReadByte();
            var kind = (StoryKind)r.R.ReadByte();
            int chapter = r.R.ReadInt32();
            if (!Authority.Handling) return; // only as a Request
            if (kind != StoryKind.Chapter && kind != StoryKind.Epilogue && kind != StoryKind.EndScreen) { Authority.Refuse("no such story step"); return; }
            if (!WorldShare.InWorld || Player.Instance == null) { Authority.Refuse("the host isn't in its world"); return; }
            if (Singleton<Dreams>.Instance != null && Singleton<Dreams>.Instance.dreaming) { Authority.Refuse("the host is in a dream"); return; }
            if (_chapterSent || Generating || _endSent) { Authority.Refuse("the host's game is already on its way there"); return; }
            int now = HostChapter;
            if (kind == StoryKind.Chapter && chapter != now + 1) { Authority.Refuse($"chapter {chapter} isn't the next one (this is chapter {now})"); return; }
            if (kind == StoryKind.Epilogue && now < LastChapter) { Authority.Refuse($"the epilogue comes after chapter {LastChapter} (this is chapter {now})"); return; }
            var ge = obj.Valid ? ObjRef.Resolve<GameEvents>(obj) : null;
            if (ge != null)
            {
                var step = index < ge.events.Count ? ge.events[index] : null;
                if (KindOf(step) != kind || (kind == StoryKind.Chapter && step.intValue != chapter)) { Authority.Refuse($"{ge.name} doesn't do that"); return; }
                if (!Authority.InReach(ge.transform.position, 3000f, ge.name)) return;
            }
            else if (!(Sync.PlayerSync.Instance != null && Sync.PlayerSync.Instance.Remotes.TryGetValue(from, out var rp) && rp.Away))
            {
                Authority.Refuse("that event isn't in the host's world, and they aren't in a place of their own");
                return;
            }
            Plugin.Log.LogInfo($"{s.NameOf(from)} set off {(kind == StoryKind.Chapter ? $"chapter {chapter}" : $"the game's end ({kind})")}: running it here");
            try { Singleton<Controller>.Instance.StartCoroutine(StepFor(kind, chapter).fire(Player.Instance.gameObject)); }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't run the story step: {e.Message}"); Authority.Refuse("it couldn't run"); }
        }

        // ---- host --------------------------------------------------------------------------------------

        private static void SendChapterStart(int chapter)
        {
            var s = S;
            if (s == null || !s.IsHost || s.Peers.Count == 0 || _chapterSent) return;
            _chapterSent = true;
            Plugin.Log.LogInfo($"Starting chapter {chapter}: the players in your world follow once it's ready");
            var w = s.Begin(MsgType.ChapterStart);
            w.W.Write(chapter);
            s.Broadcast(w, Channel.Control, reliable: true);
        }

        /// <summary>Controller.generateChapter: a joiner's game doesn't build a chapter itself; the host tells the joiners.</summary>
        internal static bool BeforeGenerate(int chapter, bool generateSave)
        {
            var s = S;
            if (s == null || !s.Active || !generateSave) return true; // loading a chapter's save (also a joiner loading the host's)
            if (s.IsClient)
            {
                Plugin.Log.LogWarning($"Not starting chapter {chapter} here: the host's game starts chapters");
                return false;
            }
            SendChapterStart(chapter);
            Generating = true;
            return true;
        }

        internal static void HostEnding(EndKind kind)
        {
            var s = S;
            if (s == null || !s.IsHost || _endSent) return;
            _endSent = true;
            Plugin.Log.LogInfo($"The game's end ({kind}): the players in your world see it too, and the server stops");
            var w = s.Begin(MsgType.GameEnd);
            w.W.Write((byte)kind);
            WorldShare.Instance?.StopServer(w);
        }

        internal static void OnPrepareDream(string preset) { if (preset == EpilogueDream) HostEnding(EndKind.Epilogue); }
        internal static void OnShowEndGame() => HostEnding(EndKind.EndScreen);

        // ---- joiner ------------------------------------------------------------------------------------

        private static float _chapterWaitUntil;

        private static void OnChapterStart(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || s.IsHost || from != s.HostId) return;
            int chapter = r.R.ReadInt32();
            Characters.Instance?.SaveNow(); // before the host's game saves and moves on
            Plugin.Notice($"The host is starting chapter {chapter}. You'll follow once it's ready.");
            if (!WorldShare.InWorld) return;
            try
            {
                Singleton<UI>.Instance.tweenBlackScreen(Color.black, 1f);
                Player.Instance.halt();
                Core.forbidInputs = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't fade out for the chapter: {e.Message}"); }
            bool waiting = _chapterWaitUntil > Time.realtimeSinceStartup;
            _chapterWaitUntil = Time.realtimeSinceStartup + 300f;
            if (!waiting) Plugin.Instance.StartCoroutine(ChapterWait(SceneManager.GetActiveScene().handle));
        }

        /// <summary>If the host's WorldReload never comes (its chapter failed), let our player go on after a while.</summary>
        private static IEnumerator ChapterWait(int scene)
        {
            while (Time.realtimeSinceStartup < _chapterWaitUntil)
            {
                if (SceneManager.GetActiveScene().handle != scene || !WorldShare.InWorld) { _chapterWaitUntil = 0f; yield break; }
                yield return null;
            }
            Plugin.Notice("The host's next chapter didn't come");
            Core.forbidInputs = false;
            Singleton<UI>.Instance.tweenBlackScreen(new Color(0f, 0f, 0f, 0f), 1f);
        }

        private static void OnGameEnd(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || s.IsHost || from != s.HostId) return;
            var kind = (EndKind)r.R.ReadByte();
            Plugin.Log.LogInfo($"The host reached the game's end ({kind})");
            s.Backend?.Leave();
            WorldShare.Instance?.SetLoadStatus("The host reached the end of the game.");
            if (!WorldShare.InWorld || !SaveRedirect.Active) return;
            Plugin.Notice("The host has reached the end of the game");
            Plugin.Instance.StartCoroutine(ShowEnding(kind));
        }

        /// <summary>
        /// Joiner, out of the session: the ending in our copy of the host's world. Saves still go to the mod's cache
        /// (SaveRedirect lasts until the main menu).
        /// </summary>
        private static IEnumerator ShowEnding(EndKind kind)
        {
            yield return new WaitForSecondsRealtime(2f); // the notice
            if (kind == EndKind.Epilogue)
            {
                bool started = false;
                try { started = StartEpilogue(); }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't start the epilogue: {e.Message}"); }
                if (started)
                {
                    float until = Time.realtimeSinceStartup + 45f;
                    while (Time.realtimeSinceStartup < until && !(Player.Instance != null && Player.Instance.inEpilogue)) yield return null;
                }
                if (started && Player.Instance != null && Player.Instance.inEpilogue)
                {
                    Plugin.Log.LogInfo("Playing the epilogue (the credits follow, then the main menu)");
                    if (!TestQuick) { yield return ToMenuAfterEnding(); yield break; }
                    yield return new WaitForSecondsRealtime(5f);
                    Plugin.Log.LogInfo("Test: skipping the rest of the epilogue");
                }
                else
                {
                    Plugin.Log.LogInfo("The epilogue didn't start here: showing the end screen");
                    yield return EndScreen();
                }
            }
            else yield return EndScreen();
            ToCredits();
            yield return ToMenuAfterEnding();
        }

        /// <summary>As the epilogue's startDream step does it: inputs off, a black screen, then the epilogue dream.</summary>
        private static bool StartEpilogue()
        {
            var dreams = Singleton<Dreams>.Instance;
            var p = Player.Instance;
            if (dreams == null || p == null || dreams.dreaming) return false;
            if (Singleton<OutsideLocations>.Instance != null && Singleton<OutsideLocations>.Instance.playerInOutsideLocation) return false;
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
            try { Singleton<UI>.Instance.showEndGame(); }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't show the end screen: {e.Message}"); }
            Plugin.Log.LogInfo("Showing the end screen");
            yield return new WaitForSecondsRealtime(TestQuick ? 4f : 12f);
        }

        /// <summary>As EpilogueOutcomes.goToCredits does: fade out, then the credits scene, which ends at the main menu.</summary>
        private static void ToCredits()
        {
            Plugin.Log.LogInfo("To the credits");
            try
            {
                Core.unpause();
                Singleton<Controller>.Instance.fadeAudio(fadeOut: true, 3f, musicToo: true);
                Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 3f);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't fade out for the credits: {e.Message}"); }
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

        private static IEnumerator ToMenuAfterEnding()
        {
            bool credits = false;
            while (!WorldShare.MainMenuReady())
            {
                if (!credits && SceneManager.GetActiveScene().name == "credits")
                {
                    credits = true;
                    Plugin.Log.LogInfo("The credits are rolling");
                    if (TestQuick) Plugin.Instance.StartCoroutine(SkipCredits());
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
            if (c != null) AccessTools.Method(typeof(Credits), "exitToMainMenu").Invoke(c, null);
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.generateChapter))]
    static class GenerateChapterPatch
    {
        static bool Prefix(int _chapterId, bool generateSave) => Chapters.BeforeGenerate(_chapterId, generateSave);
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.prepareDream))]
    static class EpilogueStartPatch
    {
        static void Prefix(string presetName) => Chapters.OnPrepareDream(presetName);
    }

    [HarmonyPatch(typeof(UI), nameof(UI.showEndGame))]
    static class EndScreenPatch
    {
        static void Prefix() => Chapters.OnShowEndGame();
    }
}
