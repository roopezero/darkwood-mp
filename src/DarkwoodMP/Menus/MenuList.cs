using System;
using System.Collections.Generic;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The main menu's and the pause menu's entries as Half-Life's main menu
    /// has them: a column of words in the lower left, in the game's pixel font
    /// (PixelFont, drawn larger than the co-op windows' text), over the game's
    /// own backdrop, logo and version lines (MenuLayout). The game's own
    /// entries (pictures of words, tk2d buttons) stay in the menu but are
    /// hidden and can't be clicked; the entries here call what they did, so the
    /// game's screens (profiles, options, help, credits) open as before. The
    /// list is shown only while the menu's first page (MainMenu.Menu0) is, with
    /// none of the game's boxes over it, and fades with the logo and the black
    /// screen.
    ///
    /// Main menu: Start Game, Join Game (the server browser; Plugin.OpenJoin),
    /// Options, Credits, Quit.
    /// Pause menu: Resume, Multiplayer (the co-op panel), Help (the game's
    /// controls pages), Options, Back to main menu, Quit. Leaving a world asks
    /// first (ConfirmDialog), as the game did; quitting from the main menu asks
    /// too.
    ///
    /// Mouse: hover lights a word, a click runs it. Keyboard and controller: up
    /// and down (arrows, W/S, the d-pad) choose, Enter (or A) runs it; Escape
    /// is the game's (it closes the pause menu).
    /// </summary>
    public sealed partial class MenuList
    {
        private sealed class Entry
        {
            public string Label;
            public Action Run;
            public Rect Rect; // where it was last drawn, in GUI pixels
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private bool _pause;
        // chosen with the keyboard or controller (-1: none, the mouse decides)
        private int _selected = -1;
        // a click from OnGUI, run in the next Update
        private Action _pending;
        private float _alpha;
        private bool _shown;
        private float _nextPad;
        private Vector2 _lastMouse;

        /// <summary>The list is on the screen now.</summary>
        public bool Shown => _shown;

        /// <summary>
        /// Screen pixels per font pixel for the entries: 3 at 1080 lines, 2 at
        /// 720.
        /// </summary>
        private int TextScale =>
            Mathf.Max(1, Mathf.RoundToInt(Screen.height / 360f));

        // the last entry's middle above the bottom, as a share of the height
        private const float Bottom = 0.20f;
        // the distance between entries, in lines
        private const float RowLines = 1.6f;

        /// <summary>
        /// Every frame (Plugin.Update): hide the game's entries, work out the
        /// list, take keys, run a click.
        /// </summary>
        public void Update()
        {
            var mm = Singleton<MainMenu>.Instance;
            _shown = Visible(mm, out _alpha);
            if (mm != null && mm.Menu0 != null && mm.Menu0.activeInHierarchy)
            {
                HideGameButtons(mm);
            }

            if (!_shown)
            {
                _selected = -1;
                _pending = null;
                return;
            }

            bool pause = InWorldMenu();
            if (pause != _pause || _entries.Count == 0)
            {
                _pause = pause;
                Build();
                _selected = -1;
            }

            if (!MenuWindows.AnyOpen)
            {
                Keys();
            }

            if (_pending != null)
            {
                var act = _pending;
                _pending = null;
                try
                {
                    act();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[menu] an entry failed: {e.Message}");
                }
            }
        }

        private bool InWorldMenu() =>
            Singleton<WorldGenerator>.Instance != null;

        private void Build()
        {
            _entries.Clear();
            if (_pause)
            {
                Add(
                    "Resume",
                    () => GameButton(
                        Go(Singleton<MainMenu>.Instance.ResumeBtn)));
                Add("Multiplayer", () => Plugin.Instance.OpenPanel());
                Add(
                    "Help",
                    () => GameButton(Go(Singleton<MainMenu>.Instance.HelpBtn)));
                Add("Options", Options);
                Add(
                    "Back to main menu",
                    () => ConfirmDialog.Ask(
                        "Back to main menu",
                        "Leave this world for the main menu? " + LeaveText(),
                        "Yes", ToMainMenu));
                Add(
                    "Quit",
                    () => ConfirmDialog.Ask(
                        "Quit", "Quit Darkwood? " + LeaveText(), "Yes", Quit));
            }
            else
            {
                Add("Start Game", StartGame);
                Add("Join Game", () => Plugin.Instance.OpenJoin());
                Add("Options", Options);
                Add(
                    "Credits",
                    () => GameButton(Singleton<MainMenu>.Instance.creditsBtn));
                Add(
                    "Quit",
                    () => ConfirmDialog.Ask(
                        "Quit", "Quit Darkwood?", "Yes", Quit));
            }
        }

        private void Add(string label, Action run) =>
            _entries.Add(new Entry { Label = label, Run = run });

        private void StartGame()
        {
            if (MenuWindows.OpenStartGame != null)
            {
                MenuWindows.OpenStartGame();
            }
            else
            {
                GameButton(Singleton<MainMenu>.Instance.playBtn);
            }
        }

        private void Options()
        {
            if (MenuWindows.OpenOptions != null)
            {
                MenuWindows.OpenOptions();
            }
            else
            {
                GameButton(Singleton<MainMenu>.Instance.optionsBtn);
            }
        }

        /// <summary>
        /// Click one of the game's own (hidden) entries, as a mouse click on it
        /// does (Button.getClicked).
        /// </summary>
        private void GameButton(GameObject go)
        {
            global::Button b = null;
            if (go != null)
            {
                b = go.GetComponent<global::Button>();
            }

            if (b == null)
            {
                string name = null;
                if (go != null)
                {
                    name = go.name;
                }

                if (name == null)
                {
                    name = "entry";
                }

                Plugin.Log.LogWarning($"[menu] the game's {name} isn't there");
                return;
            }

            b.getClicked(force: true);
        }

        /// <summary>
        /// What leaving the world loses, as the game's own box said it (the
        /// time since the last save): the core's LeaveRules.
        /// </summary>
        private string LeaveText()
        {
            return LeaveRules.Text(LeaveOf());
        }

        /// The facts for LeaveRules: whose world this is, when it was last
        /// saved and who else is in the game.
        private LeaveFacts LeaveOf()
        {
            WorldShare instance = WorldShare.Instance;
            bool copy = instance != null && instance.PlayingHostCopy;
            bool known = false;
            DateTime saved = default;
            if (!copy)
            {
                try
                {
                    saved = Singleton<SaveManager>.Instance.lastTimeSaved;
                    known = true;
                }
                catch
                {
                    known = false;
                }
            }

            Plugin plugin = Plugin.Instance;
            Net.Session session = null;
            if (plugin != null)
            {
                session = plugin.Host.Session;
            }

            bool host = session != null && session.IsHost;
            int peers = 0;
            if (host)
            {
                peers = session.Peers.Count;
            }

            return new LeaveFacts
            {
                HostCopy = copy,
                SaveKnown = known,
                Now = DateTime.Now,
                Saved = saved,
                Host = host,
                Peers = peers,
                Client = session != null && session.IsClient
            };
        }

        /// <summary>
        /// As the game's box did on Yes (MainMenu.confirmBoxYes): the sound
        /// faded, then back to the main menu.
        /// </summary>
        private void ToMainMenu()
        {
            var c = Singleton<Controller>.Instance;
            c.fadeAudio(fadeOut: true, 0f, musicToo: true);
            c.waitFramesAndRun(Core.returnToMainMenu, 1);
        }

        private void Quit()
        {
            try
            {
                Controller instance = Singleton<Controller>.Instance;
                if (instance != null)
                {
                    instance.fadeAudio(fadeOut: true, 0f, musicToo: true);
                }
            }
            catch
            {
            }

            Core.quitGame();
        }

        /// <summary>
        /// The menu's first page is up with nothing of the game's over it; how
        /// visible (the logo's fade, the black screen's).
        /// </summary>
        private bool Visible(MainMenu mm, out float alpha)
        {
            alpha = 0f;
            if (mm == null || mm.Menu0 == null || !mm.Menu0.activeInHierarchy
                || !Core.mainMenu)
            {
                return false;
            }

            if (Core.forbidInputs || Plugin.Instance.Host.Loading.Up)
            {
                return false; // credits starting, a world loading
            }

            foreach (var box in new[]
                {
                    mm.confirmBox, mm.applySettingsBox, mm.newGameBox,
                    mm.resolutionBox, mm.earlyAccessBox, mm.gammaBox,
                    mm.helpBox, mm.profilesMenu, mm.gameProfileAdvanced,
                    mm.restartBox, mm.newVerBox, mm.profileErrorBox,
                    mm.profileCorruptedBox, mm.OptionsMenu
                })
            {
                if (box != null && box.activeInHierarchy)
                {
                    return false;
                }
            }

            alpha = 1f;
            Transform transform = mm.Menu0.transform.Find("Darkwood");
            tk2dBaseSprite logo = null;
            if (transform != null)
            {
                logo = transform.GetComponent<tk2dBaseSprite>();
            }

            if (logo != null)
            {
                alpha = Mathf.Min(alpha, logo.color.a);
            }

            var ui = Singleton<UI>.Instance;
            tk2dSprite black = null;
            if (ui != null && ui.blackScreenTop != null
                && ui.blackScreenTop.activeInHierarchy)
            {
                black = ui.blackScreenTop.GetComponent<tk2dSprite>();
            }

            if (black != null)
            {
                alpha = Mathf.Min(alpha, 1f - black.color.a);
            }

            return alpha > 0.02f;
        }

        /// <summary>
        /// The game's entries stay (its code places, fades and enables them)
        /// but aren't drawn and can't be clicked.
        /// </summary>
        private void HideGameButtons(MainMenu mm)
        {
            foreach (var go in GameEntries(mm))
            {
                Hide(go);
            }

            // With a controller, the game's A would click the hidden entries
            // (MainMenu.controllerButtons).
            if (mm.controllerButtons != null && mm.controllerButtons.Count > 0
                && IsMenu0List(mm))
            {
                mm.controllerButtons.Clear();
            }
        }

        /// <summary>
        /// The game's own entries in both menus (some are null in one of them).
        /// </summary>
        private GameObject[] GameEntries(MainMenu mm) =>
            new[]
            {
                mm.playBtn,
                mm.optionsBtn,
                mm.creditsBtn,
                mm.quitBtn,
                mm.mainMenuBtn,
                Go(mm.ResumeBtn),
                Go(mm.HelpBtn)
            };

        private GameObject Go(Component c)
        {
            if (c != null)
            {
                return c.gameObject;
            }

            return null;
        }

        private bool IsMenu0List(MainMenu mm)
        {
            foreach (var b in mm.controllerButtons)
            {
                if (b != null && !b.transform.IsChildOf(mm.Menu0.transform))
                {
                    return false;
                }
            }

            return true;
        }

        private readonly List<Renderer> _renderers =
            new List<Renderer>();
        private readonly List<Collider> _colliders =
            new List<Collider>();

        private void Hide(GameObject go)
        {
            if (go == null || !go.activeInHierarchy)
            {
                return;
            }

            go.GetComponentsInChildren(true, _renderers);
            foreach (var r in _renderers)
            {
                if (r.enabled)
                {
                    r.enabled = false;
                }
            }

            go.GetComponentsInChildren(true, _colliders);
            foreach (var c in _colliders)
            {
                if (c.enabled)
                {
                    c.enabled = false;
                }
            }

            var b = go.GetComponent<global::Button>();
            if (b != null && b.rolledOver)
            {
                b.rollout(force: true);
            }
        }

        // ---- drawing --------------------------------------------------------

        /// <summary>
        /// Draw the list (Plugin.OnGUI, with MenuSkin's skin, before the
        /// windows).
        /// </summary>
        public void OnGUI()
        {
            if (!_shown || _entries.Count == 0)
            {
                return;
            }

            int sc = TextScale;
            float line = PixelFont.LineAt(sc),
                step = Mathf.Round(line * RowLines);
            // the column's left edge is the logo's
            float x = Mathf.Round(Screen.width * MenuLayout.Left);
            float y = Mathf.Round(
                Screen.height * (1f - Bottom) - step * (_entries.Count - 1)
                    - line / 2f);
            bool active = !MenuWindows.AnyOpen;
            var e = Event.current;
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                if ((e.mousePosition - _lastMouse).sqrMagnitude > 1f)
                {
                    _selected = -1; // the mouse took over from the keys
                }

                _lastMouse = e.mousePosition;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                var en = _entries[i];
                float w = PixelFont.Width(en.Label, PixelFont.Kind.Title, sc);
                // The word, with a margin round it that takes the mouse too.
                en.Rect = new Rect(
                    x - 6 * sc, y - (step - line) / 2f, w + 12 * sc, step);
                bool hover = false;
                if (active)
                {
                    if (_selected >= 0)
                    {
                        hover = _selected == i;
                    }
                    else
                    {
                        hover = en.Rect.Contains(e.mousePosition);
                    }
                }

                if (active
                    && GUI.Button(en.Rect, GUIContent.none, MenuSkin.Plain))
                {
                    Fire(en);
                }

                Color c;
                if (hover)
                {
                    c = MenuSkin.Bright;
                }
                else if (active)
                {
                    c = MenuSkin.Text;
                }
                else
                {
                    c = MenuSkin.Dim;
                }

                c.a *= _alpha;
                PixelFont.Draw(
                    new Rect(x, y, w + 2 * sc, line), en.Label, c,
                    TextAnchor.MiddleLeft, PixelFont.Kind.Title, sc);
                y += step;
            }
        }
    }
}
