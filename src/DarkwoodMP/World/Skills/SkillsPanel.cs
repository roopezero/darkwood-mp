using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The Skills panel: a key (the SkillsKey setting, K by default; none of
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
    /// actions read as not pressed (SkillsPanelMouse). Escape closes the panel
    /// before the game's pause menu sees it (SkillsPanelEscape).
    /// </summary>
    public static partial class SkillsPanel
    {
        private static UiWindow _win;
        // chosen on the panel, not learned yet
        private static string _good, _negative;
        private static string _hover;
        private static string _message;          // why the last Learn didn't go
        // last drawn: icon, cell (screen, top left)
        private static readonly Dictionary<string, Rect[]> _rects =
            new Dictionary<string, Rect[]>();
        // the width the details' height was measured for
        private static float _detailW = -1f;
        private static float _detailH;
        // the Learn button as last drawn (screen, top left), for tests
        private static Rect _learnRect;

        public static bool Open => _win != null && _win.Open;

        private static KeyCode Key
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

        public static string KeyName => Key.ToString();

        private const string Title = "Skills";

        private static UiWindow Win
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

        private static bool CanOpen(Player p, out string why)
        {
            why = null;
            if (p == null || !WorldShare.InWorld || WorldShare.Loading
                || Core.mainMenu || Core.loadingGame)
            {
                why = "not in a world";
                return false;
            }

            if (Core.Paused || Core.forbidInputs || p.inMenu() || p.dying
                || p.health <= 0f)
            {
                why = "busy";
                return false;
            }

            if (Spectator.Active)
            {
                why = "watching";
                return false;
            }

            if (SkillTree.Tiers == null)
            {
                why = "no skills";
                return false;
            }

            return true;
        }

        /// <summary>Open it (with the backpack), or close it.</summary>
        public static void Toggle()
        {
            if (Open)
            {
                Win.Close();
                return;
            }

            var p = Player.Instance;
            if (!CanOpen(p, out string why))
            {
                Plugin.Log.LogInfo(
                    $"[skills] the panel can't open now ({why})");
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

        private static void Closed() =>
            Plugin.Log.LogInfo("[skills] panel closed");

        internal static void CloseFromEscape()
        {
            Plugin.Log.LogInfo("[skills] Escape: the panel closes");
            Win.Close();
        }

        /// <summary>
        /// Every frame: the key, and closing when a game menu opens, the game
        /// pauses or the world goes.
        /// </summary>
        public static void Update(bool coopPanelOpen)
        {
            var p = Player.Instance;
            if (Open
                && (!WorldShare.InWorld || Core.mainMenu || p == null
                    || p.inMenu() || Core.Paused || p.dying))
            {
                _win.Open = false;
                Plugin.Log.LogInfo(
                    "[skills] panel closed (a game menu, the pause menu, or "
                        + "the world went)");
            }

            // Escape is taken in SkillsPanelEscape, before the game's pause
            // menu sees it.
            if (coopPanelOpen || GUIUtility.keyboardControl != 0)
            {
                return;
            }

            var key = Key;
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                Toggle();
            }
        }

        public static void OnGUI()
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

        private static readonly HashSet<string> MouseActions =
            new HashSet<string>
        {
            "Primary Action",
            "Aim",
            "Secondary Attack",
            "Attack"
        };

        private static readonly bool[] _pressedOnPanel = new bool[3];
        private static int _mouseFrame = -1;
        private static bool _overPanel;
        /// <summary>
        /// For tests: reads of the game's mouse actions answered "not pressed"
        /// because of the panel.
        /// </summary>
        internal static int TestEaten;

        /// <summary>
        /// The mouse over the panel's window, and the mouse buttons pressed
        /// there and still held (once a frame).
        /// </summary>
        private static void MouseState()
        {
            if (_mouseFrame == Time.frameCount)
            {
                return;
            }

            _mouseFrame = Time.frameCount;
            var m = Input.mousePosition;
            _overPanel =
                Win.Rect.Contains(new Vector2(m.x, Screen.height - m.y));
            for (int b = 0; b < 3; b++)
            {
                if (Input.GetMouseButtonDown(b) && _overPanel)
                {
                    _pressedOnPanel[b] = true;
                }
                else if (!Input.GetMouseButton(b))
                {
                    _pressedOnPanel[b] = false;
                }
            }
        }

        /// <summary>
        /// Whether the game's read of this action should be "not pressed": a
        /// mouse action pressed over the panel, or held after a press that
        /// started there. A press that started in the world (holding aim) goes
        /// on as it was.
        /// </summary>
        internal static bool EatsAction(string action, bool down)
        {
            if (!Open || action == null || !MouseActions.Contains(action))
            {
                return false;
            }

            MouseState();
            bool held =
                _pressedOnPanel[0] || _pressedOnPanel[1] || _pressedOnPanel[2];
            bool eat = held || (down && _overPanel);
            if (eat)
            {
                TestEaten++;
            }

            return eat;
        }
    }
}
