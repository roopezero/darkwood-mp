using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Menus;
using DarkwoodMP.World.Skills;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The Skills panel (one for the game, ModHost.SkillsPanel, since it works
    /// playing alone too): a key (the SkillsKey setting, K by default; none of
    /// the game's default keys use K) opens it anywhere in the world, on the
    /// right. It doesn't open the backpack; the player can open that beside the
    /// panel with its own key. It shows the skill points to spend and the
    /// game's four tiers of skills with their own icons, names and
    /// descriptions, and takes a good skill (and a negative one when the game's
    /// rules want one) for a point (World.SkillTree). Each skill's whole cell
    /// in the grid takes the mouse, so there is no dead space between the
    /// icons, and the details under the grid have a fixed height (room for the
    /// longest description), so the window doesn't change size, or get a
    /// scrollbar that narrows everything, while the mouse moves over it.
    /// Learned skills have a bright frame and a check mark. Clicks on the panel
    /// don't reach the world: while the mouse is over it, the game's mouse
    /// actions read as not pressed (RewiredPlayerGetButtonDown,
    /// RewiredPlayerGetButton). Escape closes the panel before the game's pause
    /// menu sees it (InputScriptOnPressEsc).
    /// </summary>
    internal sealed partial class SkillsWindow
    {
        private UiWindow _win;
        // chosen on the panel, not learned yet
        private string _good, _negative;
        private string _hover;
        private string _message;          // why the last Learn didn't go
        // last drawn: icon, cell (screen, top left)
        private readonly Dictionary<string, Rect[]> _rects =
            new Dictionary<string, Rect[]>();
        // the width the details' height was measured for
        private float _detailW = -1f;
        private float _detailH;
        // the Learn button as last drawn (screen, top left), for tests
        private Rect _learnRect;
        private readonly SkillIcons _icons = new SkillIcons();

        public bool Open => _win != null && _win.Open;

        /// <summary>
        /// The panel's key (the SkillsKey setting, K by default; KeyCode.None
        /// turns it off).
        /// </summary>
        internal KeyCode Key
        {
            get
            {
                if (Plugin.Cfg != null)
                {
                    return Plugin.Cfg.SkillsKey.Value;
                }

                return KeyCode.K;
            }
        }

        public string KeyName => Key.ToString();

        private const string Title = "Skills";

        private UiWindow Win
        {
            get
            {
                if (_win != null)
                {
                    return _win;
                }

                _win = new UiWindow(0x5d17, Title)
                {
                    MinSize = new Vector2(300, 260),
                    FitContent = true,
                    // On the right, clear of the backpack (on the left) when
                    // the player opens it too.
                    DefaultRect = () => UiWindow.RightSide(
                        360 * UiWindow.S, 420 * UiWindow.S),
                    Body = Draw,
                    Footer = DrawFooter,
                    FooterHeight = 26,
                    OnClose = Closed,
                };

                return _win;
            }
        }

        /// <summary>
        /// Whether our player only watches (Spectator); never with no
        /// session.
        /// </summary>
        private static bool OnlyWatches()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Spectator.Active;
        }

        /// <summary>
        /// What the panel looks at to open, or to close by itself (the only
        /// code here that reads the game's state for the rules). HasTiers is
        /// left false: reading the tiers loads them (Toggle).
        /// </summary>
        private PanelFacts FactsOf(Player p)
        {
            if (p == null)
            {
                return new PanelFacts
                {
                    InWorld = WorldShare.InWorld,
                    Loading = WorldShare.Loading,
                    MainMenu = Core.mainMenu,
                    LoadingGame = Core.loadingGame,
                    Paused = Core.Paused,
                    ForbidInputs = Core.forbidInputs,
                    Watching = OnlyWatches(),
                    Open = Open,
                };
            }

            return new PanelFacts
            {
                HasPlayer = true,
                InWorld = WorldShare.InWorld,
                Loading = WorldShare.Loading,
                MainMenu = Core.mainMenu,
                LoadingGame = Core.loadingGame,
                Paused = Core.Paused,
                ForbidInputs = Core.forbidInputs,
                InMenu = p.inMenu(),
                Dying = p.dying,
                Health = p.health,
                Watching = OnlyWatches(),
                HasTiers = SkillTree.Tiers != null,
                Open = Open,
            };
        }

        /// <summary>Open it (with the backpack), or close it.</summary>
        public void Toggle()
        {
            if (Open)
            {
                Win.Close();
                return;
            }

            var p = Player.Instance;
            // The tiers are read only once the rest lets the panel open, as
            // reading them loads them.
            var facts = FactsOf(p);
            var note = SkillPanelRules.CanOpen(facts with { HasTiers = true });
            if (note == SkillNote.None)
            {
                note = SkillPanelRules.CanOpen(
                    facts with { HasTiers = SkillTree.Tiers != null });
            }

            if (note != SkillNote.None)
            {
                Plugin.Log.LogInfo(
                    "[skills] the panel can't open now "
                        + $"({SkillNotes.Text(note)})");
                return;
            }

            _good = _negative = _message = null;
            _rects.Clear();
            Win.ResetPlace();
            Win.Show();
            Plugin.Log.LogInfo(
                $"[skills] panel open: level {p.currentLevel}, "
                    + $"{SkillTree.Points(p)} points to spend, backpack open "
                    + $"{p.Inventory != null && p.Inventory.open}");
        }

        private void Closed() =>
            Plugin.Log.LogInfo("[skills] panel closed");

        /// <summary>
        /// Escape (InputScriptOnPressEsc): with the panel open and no film to
        /// skip, it closes the panel and the game doesn't see it. Whether it
        /// was taken.
        /// </summary>
        internal bool TakesEscape()
        {
            if (!SkillPanelRules.TakesEscape(Open, Core.forbidInputs))
            {
                return false;
            }

            Plugin.Log.LogInfo("[skills] Escape: the panel closes");
            Win.Close();
            return true;
        }

        /// <summary>
        /// Every frame: the key, and closing when a game menu opens, the game
        /// pauses or the world goes.
        /// </summary>
        public void Update(bool coopPanelOpen)
        {
            if (SkillPanelRules.ClosesNow(FactsOf(Player.Instance)))
            {
                _win.Open = false;
                Plugin.Log.LogInfo(
                    "[skills] panel closed (a game menu, the pause menu, or "
                        + "the world went)");
            }

            // Escape is taken in InputScriptOnPressEsc, before the game's pause
            // menu sees it.
            var key = Key;
            var facts = new KeyFacts
            {
                CoopMenuOpen = coopPanelOpen,
                KeyboardTaken = GUIUtility.keyboardControl != 0,
                Off = key == KeyCode.None,
            };

            if (SkillPanelRules.ReadsKey(facts) && Input.GetKeyDown(key))
            {
                Toggle();
            }
        }

        public void OnGUI()
        {
            if (!Open)
            {
                return;
            }

            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            Win.OnGUI();
            GUI.skin = skin;
        }


        // ---- the game's input while the panel is open -----------------------

        private readonly PanelPress _press = new PanelPress();
        /// <summary>
        /// For tests: reads of the game's mouse actions answered "not pressed"
        /// because of the panel.
        /// </summary>
        internal int TestEaten;

        /// <summary>
        /// The mouse over the panel's window, and the mouse buttons pressed
        /// there and still held (once a frame).
        /// </summary>
        private void MouseState()
        {
            int frame = Time.frameCount;
            if (!_press.Due(frame))
            {
                return;
            }

            var m = Input.mousePosition;
            int down = 0, held = 0;
            for (int b = 0; b < PanelPress.Buttons; b++)
            {
                if (Input.GetMouseButtonDown(b))
                {
                    down |= 1 << b;
                }

                if (Input.GetMouseButton(b))
                {
                    held |= 1 << b;
                }
            }

            _press.Note(
                new MouseFacts
                {
                    Frame = frame,
                    Over = Win.Rect.Contains(
                        new Vector2(m.x, Screen.height - m.y)),
                    Down = down,
                    Held = held,
                });
        }

        /// <summary>
        /// Whether the game's read of this action should be "not pressed": a
        /// mouse action pressed over the panel, or held after a press that
        /// started there. A press that started in the world (holding aim) goes
        /// on as it was.
        /// </summary>
        internal bool EatsAction(string action, bool down)
        {
            if (!SkillPanelRules.WatchesAction(Open, action))
            {
                return false;
            }

            MouseState();
            bool eat = _press.Eats(down);
            if (eat)
            {
                TestEaten++;
            }

            return eat;
        }
    }
}
