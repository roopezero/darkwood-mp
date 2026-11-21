using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The way in to the Skills panel (SkillsWindow) for the code that
    /// opens, draws and tests it: Plugin's frame, Progression's messages and
    /// the tests. The panel lives as long as the game (ModHost.SkillsPanel),
    /// since it works playing alone too; this class keeps no state.
    /// </summary>
    public static class SkillsPanel
    {
        private static SkillsWindow Panel => Plugin.Instance.Host.SkillsPanel;

        /// <summary>
        /// The panel's key (the SkillsKey setting, K by default; KeyCode.None
        /// turns it off).
        /// </summary>
        internal static KeyCode Key
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

        public static bool Open => Panel.Open;

        /// <summary>
        /// Every frame: the key, and closing when a game menu opens, the game
        /// pauses or the world goes.
        /// </summary>
        public static void Update(bool coopPanelOpen) =>
            Panel.Update(coopPanelOpen);

        public static void OnGUI() => Panel.OnGUI();

        internal static void CloseFromEscape() => Panel.CloseFromEscape();

        internal static bool EatsAction(string action, bool down) =>
            Panel.EatsAction(action, down);

        // ---- tests ----------------------------------------------------------

        /// <summary>For tests: the key pressed (as Update sees it).</summary>
        internal static void TestPressKey() => Panel.TestPressKey();

        /// <summary>
        /// For tests: choose and learn on the panel, as the clicks do; the
        /// result (null: learned).
        /// </summary>
        internal static string TestLearn(string good, string negative) =>
            Panel.TestLearn(good, negative);

        /// <summary>
        /// For tests: whether the Learn button would take this choice now.
        /// </summary>
        internal static bool TestReady(string good, string negative) =>
            Panel.TestReady(good, negative);

        /// <summary>For tests: pick these skills, as clicks would.</summary>
        internal static void TestPick(string good, string negative) =>
            Panel.TestPick(good, negative);

        /// <summary>
        /// For tests: the mouse as the panel sees it (screen pixels from the
        /// top left); null: the real mouse.
        /// </summary>
        internal static Vector2? TestMouse
        {
            get => Panel.TestMouse;
            set => Panel.TestMouse = value;
        }

        /// <summary>For tests: the skill the mouse is over.</summary>
        internal static string TestHover => Panel.TestHover;

        /// <summary>For tests: the good skill picked, not learned.</summary>
        internal static string TestChosen => Panel.TestChosen;

        /// <summary>
        /// For tests: reads of the game's mouse actions answered "not pressed"
        /// because of the panel.
        /// </summary>
        internal static int TestEaten => Panel.TestEaten;

        /// <summary>For tests: the window's place and size.</summary>
        internal static Rect TestWindow => Panel.TestWindow;

        /// <summary>
        /// For tests: a skill's icon and its cell as last drawn, in screen
        /// pixels from the top left.
        /// </summary>
        internal static bool TestRects(
            string skill, out Rect icon, out Rect cell) =>
            Panel.TestRects(skill, out icon, out cell);

        /// <summary>For tests: SkillsWindow.TestHoverSweep.</summary>
        internal static IEnumerator TestHoverSweep(Action<int> done) =>
            Panel.TestHoverSweep(done);

        /// <summary>For tests: SkillsWindow.TestClickAll.</summary>
        internal static IEnumerator TestClickAll(
            Action<List<string>> done) =>
            Panel.TestClickAll(done);
    }
}
