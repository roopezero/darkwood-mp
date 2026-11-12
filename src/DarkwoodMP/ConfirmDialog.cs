using System;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// A small modal question in the co-op windows' style ("Quit Darkwood?" with Yes and No), in the middle of the
    /// screen. Enter answers Yes, Escape answers No, and so does its close box. While it's open the windows under it
    /// take no clicks and the game's menu buttons and Escape are off (Plugin: MenuWindows.AnyOpen). The answer's
    /// action runs in the next Update, outside OnGUI, once the game's buttons are on again. One question at a time:
    /// a new one replaces the one that's open.
    /// </summary>
    public static class ConfirmDialog
    {
        private static UiWindow _win;
        private static string _text, _yes, _no;
        private static Action _onYes, _onNo, _pending;
        private static float _deadline; // realtime when No is chosen by itself (0: never)

        public static bool Open => _win != null && _win.Open;

        /// <summary>
        /// Ask: <paramref name="title"/> in the title bar, <paramref name="text"/> (may be null) wrapped in the body,
        /// <paramref name="yes"/> on the button that runs <paramref name="onYes"/>. <paramref name="onNo"/>, if given,
        /// runs when the answer is No (the No button, Escape or the close box). With <paramref name="timeout"/> above 0,
        /// No is chosen by itself after that many seconds, and the body counts them down ("keep these settings?").
        /// </summary>
        public static void Ask(string title, string text, string yes, Action onYes, string no = "No", Action onNo = null, float timeout = 0f)
        {
            if (_win == null)
            {
                float S() => PixelFont.Scale;
                _win = new UiWindow(0x0D40, "")
                {
                    Modal = true,
                    FitContent = true,
                    MinSize = new Vector2(240, 90),
                    DefaultRect = () => new Rect((Screen.width - 300 * S()) / 2f, Screen.height * 0.38f, 300 * S(), 110 * S()),
                    Body = DrawBody,
                    Footer = DrawButtons,
                    FooterHeight = 13 + 8,
                    OnClose = () => Answer(false),
                };
            }
            _win.Title = title ?? "";
            _text = text; _yes = string.IsNullOrEmpty(yes) ? "Yes" : yes; _no = string.IsNullOrEmpty(no) ? "No" : no;
            _onYes = onYes; _onNo = onNo;
            _deadline = timeout > 0f ? Time.realtimeSinceStartup + timeout : 0f;
            _win.ResetPlace();
            _win.Show();
            Plugin.Log.LogInfo($"[confirm] asked: {title}");
        }

        /// <summary>Close it as if No was chosen (between tests, or when what it asked about went away).</summary>
        public static void Cancel() { if (Open) _win.Close(); }

        /// <summary>Choose an answer, as the buttons do (for the tests).</summary>
        public static void Choose(bool yes) { if (Open) Answer(yes); }

        private static void Answer(bool yes)
        {
            var act = yes ? _onYes : _onNo;
            _onYes = _onNo = null;
            _deadline = 0f;
            if (_win.Open) { _win.OnClose = null; _win.Close(); _win.OnClose = () => Answer(false); }
            _pending = act;
            Plugin.Log.LogInfo($"[confirm] answered {(yes ? "yes" : "no")}: {_win.Title}");
        }

        /// <summary>Every frame (Plugin.Update, after the game's buttons are let go): run the answer's action.</summary>
        public static void Update()
        {
            if (Open && _deadline > 0f && Time.realtimeSinceStartup >= _deadline) { _deadline = 0f; Answer(false); }
            if (_pending == null) return;
            var act = _pending;
            _pending = null;
            try { act(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[confirm] the answer's action failed: {e.Message}"); }
        }

        /// <summary>Draw it (Plugin.OnGUI, with MenuSkin's skin, after every other window).</summary>
        public static void OnGUI()
        {
            if (!Open) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.Use(); Answer(true); return; }
                if (e.keyCode == KeyCode.Escape) { e.Use(); Answer(false); return; }
            }
            // The screen behind it a little darker, so it reads as a question that has to be answered.
            if (e.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), MenuSkin.White);
                GUI.color = old;
            }
            _win.OnGUI();
        }

        private static void DrawBody(Flow f)
        {
            f.Space(4);
            if (!string.IsNullOrEmpty(_text)) Ui.Text(f, _text);
            if (_deadline > 0f)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(_deadline - Time.realtimeSinceStartup));
                Ui.Text(f, $"{_no} in {left} second{(left == 1 ? "" : "s")}.", MenuSkin.Dim);
            }
        }

        private static void DrawButtons(Rect r)
        {
            var b = Ui.Split(r, 0, 80, 80);
            if (Ui.Button(b[1], _yes)) Answer(true);
            else if (Ui.Button(b[2], _no)) Answer(false);
        }
    }
}
