using System;
using System.Collections.Generic;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// Hooks for the mod's own menu windows, so the main menu and the pause menu (MenuList) can open them and know
    /// when one is up.
    ///
    /// - <see cref="OpenStartGame"/>: the main menu's Start Game. Until it's set, Start Game opens the game's own
    ///   profiles screen (continue a profile or start a new one).
    /// - <see cref="OpenOptions"/>: Options in both menus. Until it's set, the game's own options screen.
    /// - <see cref="Register"/>: a window says how to tell it's open. While any is (or the co-op panel, or a
    ///   ConfirmDialog), the menu's list takes no clicks or keys, the game's own menu buttons and its Escape are off,
    ///   and the system mouse cursor is shown.
    /// - <see cref="Draw"/>: called from the mod's OnGUI with MenuSkin's skin set, after the menu's list and the
    ///   co-op panel and before a ConfirmDialog (which stays on top). Draw the window's UiWindow there.
    /// </summary>
    public static class MenuWindows
    {
        public static Action OpenStartGame;
        public static Action OpenOptions;
        public static event Action Draw;

        private static readonly List<Func<bool>> _open = new List<Func<bool>>();

        public static void Register(Func<bool> isOpen) { if (isOpen != null) _open.Add(isOpen); }

        /// <summary>One of the mod's windows is up over the menus (a registered one, the co-op panel or a ConfirmDialog).</summary>
        public static bool AnyOpen
        {
            get
            {
                if (ConfirmDialog.Open || (Plugin.Instance != null && Plugin.Instance.PanelOpen)) return true;
                foreach (var f in _open)
                {
                    try { if (f()) return true; } catch { }
                }
                return false;
            }
        }

        internal static void DrawAll()
        {
            if (Draw == null) return;
            foreach (Action d in Draw.GetInvocationList())
            {
                try { d(); }
                catch (Exception e) { Plugin.Log.LogWarning($"A menu window failed to draw: {e.Message}"); }
            }
        }
    }

    /// <summary>
    /// The main menu's and the pause menu's entries as Half-Life's main menu has them: a column of words in the lower
    /// left, in the game's pixel font (PixelFont, drawn larger than the co-op windows' text), over the game's own
    /// backdrop, logo and version lines (MenuLayout). The game's own entries (pictures of words, tk2d buttons) stay in
    /// the menu but are hidden and can't be clicked; the entries here call what they did, so the game's screens
    /// (profiles, options, help, credits) open as before. The list is shown only while the menu's first page
    /// (MainMenu.Menu0) is, with none of the game's boxes over it, and fades with the logo and the black screen.
    ///
    /// Main menu: Start Game, Join Game (the server browser; Plugin.OpenJoin), Options, Credits, Quit.
    /// Pause menu: Resume, Multiplayer (the co-op panel), Help (the game's controls pages), Options, Back to main menu,
    /// Quit. Leaving a world asks first (ConfirmDialog), as the game did; quitting from the main menu asks too.
    ///
    /// Mouse: hover lights a word, a click runs it. Keyboard and controller: up and down (arrows, W/S, the d-pad)
    /// choose, Enter (or A) runs it; Escape is the game's (it closes the pause menu).
    /// </summary>
    public static class MenuList
    {
        private sealed class Entry
        {
            public string Label;
            public Action Run;
            public Rect Rect; // where it was last drawn, in GUI pixels
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _pause;
        private static int _selected = -1;     // chosen with the keyboard or controller (-1: none, the mouse decides)
        private static Action _pending;        // a click from OnGUI, run in the next Update
        private static float _alpha;
        private static bool _shown;
        private static float _nextPad;
        private static Vector2 _lastMouse;

        /// <summary>The list is on the screen now.</summary>
        public static bool Shown => _shown;

        /// <summary>Screen pixels per font pixel for the entries: 3 at 1080 lines, 2 at 720.</summary>
        private static int TextScale => Mathf.Max(1, Mathf.RoundToInt(Screen.height / 360f));

        private const float Left = 0.065f;   // the column's left edge, as a share of the width (the logo's, MenuLayout)
        private const float Bottom = 0.20f;  // the last entry's middle above the bottom, as a share of the height
        private const float RowLines = 1.6f; // the distance between entries, in lines

        /// <summary>Every frame (Plugin.Update): hide the game's entries, work out the list, take keys, run a click.</summary>
        public static void Update()
        {
            var mm = Singleton<MainMenu>.Instance;
            _shown = Visible(mm, out _alpha);
            if (mm != null && mm.Menu0 != null && mm.Menu0.activeInHierarchy) HideGameButtons(mm);
            if (!_shown) { _selected = -1; _pending = null; return; }
            bool pause = InWorldMenu();
            if (pause != _pause || _entries.Count == 0) { _pause = pause; Build(); _selected = -1; }

            if (!MenuWindows.AnyOpen) Keys();
            if (_pending != null)
            {
                var act = _pending;
                _pending = null;
                try { act(); }
                catch (Exception e) { Plugin.Log.LogWarning($"[menu] an entry failed: {e.Message}"); }
            }
        }

        private static bool InWorldMenu() => Singleton<WorldGenerator>.Instance != null;

        private static void Build()
        {
            _entries.Clear();
            if (_pause)
            {
                Add("Resume", () => GameButton(Singleton<MainMenu>.Instance.ResumeBtn != null ? Singleton<MainMenu>.Instance.ResumeBtn.gameObject : null));
                Add("Multiplayer", () => Plugin.Instance.OpenPanel());
                Add("Help", () => GameButton(Singleton<MainMenu>.Instance.HelpBtn != null ? Singleton<MainMenu>.Instance.HelpBtn.gameObject : null));
                Add("Options", Options);
                Add("Back to main menu", () => ConfirmDialog.Ask("Back to main menu", "Leave this world for the main menu? " + LeaveText(), "Yes", ToMainMenu));
                Add("Quit", () => ConfirmDialog.Ask("Quit", "Quit Darkwood? " + LeaveText(), "Yes", Quit));
            }
            else
            {
                Add("Start Game", StartGame);
                Add("Join Game", () => Plugin.Instance.OpenJoin());
                Add("Options", Options);
                Add("Credits", () => GameButton(Singleton<MainMenu>.Instance.creditsBtn));
                Add("Quit", () => ConfirmDialog.Ask("Quit", "Quit Darkwood?", "Yes", Quit));
            }
        }

        private static void Add(string label, Action run) => _entries.Add(new Entry { Label = label, Run = run });

        private static void StartGame()
        {
            if (MenuWindows.OpenStartGame != null) MenuWindows.OpenStartGame();
            else GameButton(Singleton<MainMenu>.Instance.playBtn);
        }

        private static void Options()
        {
            if (MenuWindows.OpenOptions != null) MenuWindows.OpenOptions();
            else GameButton(Singleton<MainMenu>.Instance.optionsBtn);
        }

        /// <summary>Click one of the game's own (hidden) entries, as a mouse click on it does (Button.getClicked).</summary>
        private static void GameButton(GameObject go)
        {
            var b = go != null ? go.GetComponent<global::Button>() : null;
            if (b == null) { Plugin.Log.LogWarning($"[menu] the game's {go?.name ?? "entry"} isn't there"); return; }
            b.getClicked(force: true);
        }

        /// <summary>What leaving the world loses, as the game's own box said it (the time since the last save).</summary>
        private static string LeaveText()
        {
            string s;
            if (WorldShare.Instance?.PlayingHostCopy == true) s = "This is your copy of the host's world: your own saves are untouched.";
            else
            {
                try
                {
                    var since = DateTime.Now - Singleton<SaveManager>.Instance.lastTimeSaved;
                    string ago = since.TotalHours >= 1 ? $"{(int)since.TotalHours} h {since.Minutes} min" : since.TotalMinutes >= 1 ? $"{(int)since.TotalMinutes} min" : $"{Math.Max(0, since.Seconds)} s";
                    s = $"What you did since the last save ({ago} ago) will be lost.";
                }
                catch { s = "What you did since the last save will be lost."; }
            }
            var session = Plugin.Instance?.Session;
            if (session != null && session.IsHost && session.Peers.Count > 0) s += " The players in your game go back to their menus.";
            else if (session != null && session.IsClient) s += " You leave the host's game.";
            return s;
        }

        /// <summary>As the game's box did on Yes (MainMenu.confirmBoxYes): the sound faded, then back to the main menu.</summary>
        private static void ToMainMenu()
        {
            var c = Singleton<Controller>.Instance;
            c.fadeAudio(fadeOut: true, 0f, musicToo: true);
            c.waitFramesAndRun(Core.returnToMainMenu, 1);
        }

        private static void Quit()
        {
            try { Singleton<Controller>.Instance?.fadeAudio(fadeOut: true, 0f, musicToo: true); } catch { }
            Core.quitGame();
        }

        /// <summary>The menu's first page is up with nothing of the game's over it; how visible (the logo's fade, the black screen's).</summary>
        private static bool Visible(MainMenu mm, out float alpha)
        {
            alpha = 0f;
            if (mm == null || mm.Menu0 == null || !mm.Menu0.activeInHierarchy || !Core.mainMenu) return false;
            if (Core.forbidInputs || LoadingScreen.Up) return false; // credits starting, a world loading
            foreach (var box in new[] { mm.confirmBox, mm.applySettingsBox, mm.newGameBox, mm.resolutionBox, mm.earlyAccessBox, mm.gammaBox, mm.helpBox,
                                        mm.profilesMenu, mm.gameProfileAdvanced, mm.restartBox, mm.newVerBox, mm.profileErrorBox, mm.profileCorruptedBox, mm.OptionsMenu })
                if (box != null && box.activeInHierarchy) return false;
            alpha = 1f;
            var logo = mm.Menu0.transform.Find("Darkwood")?.GetComponent<tk2dBaseSprite>();
            if (logo != null) alpha = Mathf.Min(alpha, logo.color.a);
            var ui = Singleton<UI>.Instance;
            var black = ui != null && ui.blackScreenTop != null && ui.blackScreenTop.activeInHierarchy ? ui.blackScreenTop.GetComponent<tk2dSprite>() : null;
            if (black != null) alpha = Mathf.Min(alpha, 1f - black.color.a);
            return alpha > 0.02f;
        }

        /// <summary>The game's entries stay (its code places, fades and enables them) but aren't drawn and can't be clicked.</summary>
        private static void HideGameButtons(MainMenu mm)
        {
            Hide(mm.playBtn); Hide(mm.optionsBtn); Hide(mm.creditsBtn); Hide(mm.quitBtn); Hide(mm.mainMenuBtn);
            Hide(mm.ResumeBtn != null ? mm.ResumeBtn.gameObject : null);
            Hide(mm.HelpBtn != null ? mm.HelpBtn.gameObject : null);
            // With a controller, the game's A would click the hidden entries (MainMenu.controllerButtons).
            if (mm.controllerButtons != null && mm.controllerButtons.Count > 0 && mm.Menu0.activeInHierarchy && IsMenu0List(mm)) mm.controllerButtons.Clear();
        }

        private static bool IsMenu0List(MainMenu mm)
        {
            foreach (var b in mm.controllerButtons) if (b != null && !b.transform.IsChildOf(mm.Menu0.transform)) return false;
            return true;
        }

        private static readonly List<Renderer> _renderers = new List<Renderer>();
        private static readonly List<Collider> _colliders = new List<Collider>();

        private static void Hide(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return;
            go.GetComponentsInChildren(true, _renderers);
            foreach (var r in _renderers) if (r.enabled) r.enabled = false;
            go.GetComponentsInChildren(true, _colliders);
            foreach (var c in _colliders) if (c.enabled) c.enabled = false;
            var b = go.GetComponent<global::Button>();
            if (b != null && b.rolledOver) b.rollout(force: true);
        }

        // ---- keys and the controller ---------------------------------------------------------------

        private static void Keys()
        {
            int dir = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = -1;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = 1;
            bool run = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            try
            {
                var pad = Singleton<InputScript>.Instance?.rewiredPlayer;
                if (pad != null)
                {
                    float v = pad.GetAxisRaw("Dpad Vertical");
                    if (Mathf.Abs(v) > 0.5f && Time.unscaledTime >= _nextPad) { dir = v > 0 ? -1 : 1; _nextPad = Time.unscaledTime + 0.25f; }
                    else if (Mathf.Abs(v) <= 0.5f) _nextPad = 0f;
                    if (pad.GetButtonDown("A")) run = true;
                }
            }
            catch { }
            if (dir != 0 && _entries.Count > 0)
                _selected = _selected < 0 ? (dir > 0 ? 0 : _entries.Count - 1) : (_selected + dir + _entries.Count) % _entries.Count;
            if (run && _selected >= 0 && _selected < _entries.Count) Fire(_entries[_selected]);
        }

        private static void Fire(Entry e)
        {
            Plugin.Log.LogInfo($"[menu] {e.Label}");
            _pending = e.Run;
        }

        // ---- drawing ---------------------------------------------------------------------------------

        /// <summary>Draw the list (Plugin.OnGUI, with MenuSkin's skin, before the windows).</summary>
        public static void OnGUI()
        {
            if (!_shown || _entries.Count == 0) return;
            int sc = TextScale;
            float line = PixelFont.LineAt(sc), step = Mathf.Round(line * RowLines);
            float x = Mathf.Round(Screen.width * Left);
            float y = Mathf.Round(Screen.height * (1f - Bottom) - step * (_entries.Count - 1) - line / 2f);
            bool active = !MenuWindows.AnyOpen;
            var e = Event.current;
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                if ((e.mousePosition - _lastMouse).sqrMagnitude > 1f) _selected = -1; // the mouse took over from the keys
                _lastMouse = e.mousePosition;
            }
            for (int i = 0; i < _entries.Count; i++)
            {
                var en = _entries[i];
                float w = PixelFont.Width(en.Label, PixelFont.Kind.Title, sc);
                // The word, with a margin round it that takes the mouse too.
                en.Rect = new Rect(x - 6 * sc, y - (step - line) / 2f, w + 12 * sc, step);
                bool hover = active && (_selected >= 0 ? _selected == i : en.Rect.Contains(e.mousePosition));
                if (active && GUI.Button(en.Rect, GUIContent.none, MenuSkin.Plain)) Fire(en);
                var c = hover ? MenuSkin.Bright : active ? MenuSkin.Text : MenuSkin.Dim;
                c.a *= _alpha;
                PixelFont.Draw(new Rect(x, y, w + 2 * sc, line), en.Label, c, TextAnchor.MiddleLeft, PixelFont.Kind.Title, sc);
                y += step;
            }
        }

        // ---- for the tests ---------------------------------------------------------------------------

        /// <summary>For the tests: the list, where each entry is and whether it would take a click now.</summary>
        public static string Check()
        {
            var mm = Singleton<MainMenu>.Instance;
            var parts = new List<string>();
            foreach (var en in _entries) parts.Add($"{en.Label} at {en.Rect.x:0},{en.Rect.y:0} {en.Rect.width:0}x{en.Rect.height:0}");
            return $"list shown {_shown} (alpha {_alpha:0.00}), {(_pause ? "pause menu" : "main menu")}: {string.Join("; ", parts)}; takes clicks {!MenuWindows.AnyOpen}; " +
                   $"game entries hidden {GameEntriesHidden(mm)}; screen {Screen.width}x{Screen.height}";
        }

        private static bool GameEntriesHidden(MainMenu mm)
        {
            if (mm == null) return false;
            foreach (var go in new[] { mm.playBtn, mm.optionsBtn, mm.creditsBtn, mm.quitBtn, mm.mainMenuBtn })
            {
                if (go == null || !go.activeInHierarchy) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.enabled) return false;
                foreach (var c in go.GetComponentsInChildren<Collider>()) if (c.enabled) return false;
            }
            return true;
        }

        /// <summary>For the tests: click the entry with this word, as the mouse does. False if the list hasn't got it or isn't shown.</summary>
        public static bool Click(string label)
        {
            if (!_shown || MenuWindows.AnyOpen) return false;
            foreach (var en in _entries)
                if (string.Equals(en.Label, label, StringComparison.OrdinalIgnoreCase)) { Fire(en); return true; }
            return false;
        }
    }
}
