using System;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// A small modal question in the co-op windows' style ("Quit Darkwood?"
    /// with Yes and No), in the middle of the screen. Enter answers Yes, Escape
    /// answers No, and so does its close box. While it's open the windows under
    /// it take no clicks and the game's menu buttons and Escape are off
    /// (Plugin: MenuWindows.AnyOpen). The answer's action runs in the next
    /// Update, outside OnGUI, once the game's buttons are on again. One
    /// question at a time: a new one replaces the one that's open.
    /// </summary>
    public static class ConfirmDialog
    {
        private static UiWindow _win;
        private static string _text, _yes, _no;
        private static Action _onYes, _onNo, _pending;
        // realtime when No is chosen by itself (0: never)
        private static float _deadline;

        public static bool Open => _win != null && _win.Open;

        /// <summary>
        /// Ask: <paramref name="title"/> in the title bar,
        /// <paramref name="text"/> (may be null) wrapped in the body,
        /// <paramref name="yes"/> on the button that runs
        /// <paramref name="onYes"/>. <paramref name="onNo"/>, if given, runs
        /// when the answer is No (the No button, Escape or the close box). With
        /// <paramref name="timeout"/> above 0, No is chosen by itself after
        /// that many seconds, and the body counts them down ("keep these
        /// settings?").
        /// </summary>
        public static void Ask(
            string title, string text, string yes, Action onYes,
            string no = "No", Action onNo = null, float timeout = 0f)
        {
            if (_win == null)
            {
                float S() => PixelFont.Scale;
                _win = new UiWindow(0x0D40, "")
                {
                    Modal = true,
                    FitContent = true,
                    MinSize = new Vector2(240, 90),
                    DefaultRect = () => new Rect(
                        (Screen.width - 300 * S()) / 2f, Screen.height * 0.38f,
                        300 * S(), 110 * S()),
                    Body = DrawBody,
                    Footer = DrawButtons,
                    FooterHeight = 13 + 8,
                    OnClose = Closed,
                };
            }
            string shownTitle = title;
            if (shownTitle == null)
            {
                shownTitle = "";
            }
            _win.Title = shownTitle;
            _text = text;
            if (string.IsNullOrEmpty(yes))
            {
                _yes = "Yes";
            }
            else
            {
                _yes = yes;
            }
            if (string.IsNullOrEmpty(no))
            {
                _no = "No";
            }
            else
            {
                _no = no;
            }
            _onYes = onYes;
            _onNo = onNo;
            if (timeout > 0f)
            {
                _deadline = Time.realtimeSinceStartup + timeout;
            }
            else
            {
                _deadline = 0f;
            }
            _win.ResetPlace();
            _win.Show();
            Plugin.Log.LogInfo($"[confirm] asked: {title}");
        }

        /// <summary>
        /// Close it as if No was chosen (between tests, or when what it asked
        /// about went away).
        /// </summary>
        public static void Cancel()
        {
            if (Open)
            {
                _win.Close();
            }
        }

        /// <summary>
        /// Choose an answer, as the buttons do (for the tests).
        /// </summary>
        public static void Choose(bool yes)
        {
            if (Open)
            {
                Answer(yes);
            }
        }

        // The close box answers No.
        private static void Closed() => Answer(false);

        private static void Answer(bool yes)
        {
            Action act;
            if (yes)
            {
                act = _onYes;
            }
            else
            {
                act = _onNo;
            }
            _onYes = _onNo = null;
            _deadline = 0f;
            if (_win.Open)
            {
                _win.OnClose = null;
                _win.Close();
                _win.OnClose = Closed;
            }
            _pending = act;
            string text = "no";
            if (yes)
            {
                text = "yes";
            }
            Plugin.Log.LogInfo($"[confirm] answered {text}: {_win.Title}");
        }

        /// <summary>
        /// Every frame (Plugin.Update, after the game's buttons are let go):
        /// run the answer's action.
        /// </summary>
        public static void Update()
        {
            if (Open && _deadline > 0f
                && Time.realtimeSinceStartup >= _deadline)
            {
                _deadline = 0f;
                Answer(false);
            }
            if (_pending == null)
            {
                return;
            }
            var act = _pending;
            _pending = null;
            try
            {
                act();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[confirm] the answer's action failed: {e.Message}");
            }
        }

        /// <summary>
        /// Draw it (Plugin.OnGUI, with MenuSkin's skin, after every other
        /// window).
        /// </summary>
        public static void OnGUI()
        {
            if (!Open)
            {
                return;
            }
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return
                    || e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    Answer(true);
                    return;
                }
                if (e.keyCode == KeyCode.Escape)
                {
                    e.Use();
                    Answer(false);
                    return;
                }
            }
            // The screen behind it a little darker, so it reads as a question
            // that has to be answered.
            if (e.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(
                    new Rect(0, 0, Screen.width, Screen.height),
                    MenuSkin.White);
                GUI.color = old;
            }
            _win.OnGUI();
        }

        private static void DrawBody(Flow f)
        {
            f.Space(4);
            if (!string.IsNullOrEmpty(_text))
            {
                Ui.Text(f, _text);
            }
            if (_deadline > 0f)
            {
                int left = Mathf.Max(
                    0, Mathf.CeilToInt(_deadline - Time.realtimeSinceStartup));
                string text = "s";
                if (left == 1)
                {
                    text = "";
                }
                Ui.Text(f, $"{_no} in {left} second{text}.", MenuSkin.Dim);
            }
        }

        private static void DrawButtons(Rect r)
        {
            var b = Ui.Split(r, 0, 80, 80);
            if (Ui.Button(b[1], _yes))
            {
                Answer(true);
            }
            else if (Ui.Button(b[2], _no))
            {
                Answer(false);
            }
        }
    }
}
