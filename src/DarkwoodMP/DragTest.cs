using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// "--dwmp-test-drag" (Plugin): moving and resizing a UiWindow with made-up mouse events, in the main menu (issue
    /// #18: slow drags felt stuck). Each case presses on the title bar (or the grip), moves the mouse in many small steps
    /// or a few big ones, one event a frame, lets go, and checks that the window moved (or grew) exactly as far as the
    /// mouse did. The moves and the release go through UiWindow.OnGUI as real ones do, and the window reads the mouse
    /// from them (UiWindow.TestMouse) instead of from Input. The press on the title bar is started with UiWindow.BeginDrag: Unity calls a window function with the frame's real event, and a made-up event
    /// put in place inside it arrived as an Ignore event, so that press (and Unity's GUI.DragWindow) can't be tried this
    /// way. Logs "[drag]" lines, ending with "[drag] done".
    /// </summary>
    internal static class DragTest
    {
        private static void Log(string s) => Plugin.Log.LogInfo("[drag] " + s);
        private static UiWindow _win;
        private static readonly Queue<Event> _events = new Queue<Event>();
        private static int _fails;

        /// <summary>Called last in Plugin.OnGUI. One made-up event replaces each frame's Repaint, before the window takes it.</summary>
        public static void OnGUI()
        {
            if (_win == null || !_win.Open) return;
            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            if (_real && Event.current.isMouse) { _lastEv = Event.current.mousePosition; if (Event.current.type == EventType.MouseDrag) _drags++; }
            if (_events.Count > 0 && Event.current.type == EventType.Repaint)
            {
                Event.current = _events.Dequeue();   // the window isn't drawn this frame
                _mouse = Event.current.mousePosition;
            }
            _win.OnGUI();
            GUI.skin = skin;
        }

        private static Event Mouse(EventType type, Vector2 at, Vector2 delta)
        {
            return new Event { type = type, mousePosition = at, delta = delta, button = 0, clickCount = type == EventType.MouseDown ? 1 : 0 };
        }

        /// <summary>
        /// "--dwmp-test-drag-real": the test window stays open in the main menu for a real mouse (a tool outside the
        /// game presses on its title bar and moves). Each frame with the button held logs the frame time, the mouse
        /// (Input and the last GUI event), the window's place, how far the window is from where the mouse puts it,
        /// how many drag events came and the cursor's lock state ("[drag-real]" lines).
        /// </summary>
        public static IEnumerator RunReal()
        {
            while (!WorldShare.MainMenuReady()) yield return null;
            yield return new WaitForSecondsRealtime(2f);
            _real = true;
            _win = new UiWindow(0x0D5F, "Drag test") { DefaultRect = () => new Rect(200, 200, 500, 300), Body = f => { } };
            _win.Show();
            Plugin.Log.LogInfo($"[drag-real] ready: screen {Screen.width}x{Screen.height}, window at (200, 200), title bar to y {200 + 20 * UiWindow.S}");
            float t0 = Time.realtimeSinceStartup; bool held = false; Vector2 grab = default, start = default;
            while (Time.realtimeSinceStartup - t0 < 120f)
            {
                yield return null;
                var m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                bool down = Input.GetMouseButton(0);
                if (down && !held) { grab = m; start = _win.Rect.position; }
                if (down || held)
                    Plugin.Log.LogInfo($"[drag-real] dt {Time.unscaledDeltaTime * 1000f:0.0} mouse {m.x:0.#},{m.y:0.#} ev {_lastEv.x:0.#},{_lastEv.y:0.#} drags {_drags} win {_win.Rect.x:0.#},{_win.Rect.y:0.#} size {_win.Rect.width:0.#}x{_win.Rect.height:0.#} off {(_win.Rect.position - start - (m - grab)).x:0.#},{(_win.Rect.position - start - (m - grab)).y:0.#} lock {UnityEngine.Cursor.lockState}");
                _drags = 0;
                held = down;
            }
            _win.Close(); _win = null; _real = false;
            Plugin.Log.LogInfo("[drag-real] done");
        }

        private static bool _real;
        /// <summary>The test window is open: the mouse cursor is shown over it as over the mod's other windows.</summary>
        internal static bool WindowOpen => _win != null && _win.Open;
        private static Vector2 _mouse;
        private static int _drags;
        private static Vector2 _lastEv;

        public static IEnumerator Run()
        {
            while (!WorldShare.MainMenuReady()) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            _fails = 0;
            UiWindow.TestMouse = () => _mouse;
            _win = new UiWindow(0x0D5F, "Drag test") { DefaultRect = () => new Rect(100, 100, 500, 300), Body = f => { } };
            Log($"screen {Screen.width}x{Screen.height}, scale {UiWindow.S}");
            var cases = new (string name, bool resize, float step, int steps)[]
            {
                ("slow move, 200 steps of 1 px", false, 1f, 200),
                ("slow move, 300 steps of 0.3 px", false, 0.3f, 300),
                ("fast move, 5 steps of 40 px", false, 40f, 5),
                ("slow resize, 150 steps of 1 px", true, 1f, 150),
                ("slow resize, 300 steps of 0.3 px", true, 0.3f, 300),
                ("fast resize, 5 steps of 30 px", true, 30f, 5),
            };
            foreach (var c in cases)
            {
                _win.ResetPlace();
                _win.Show();
                yield return null; yield return null;
                var before = _win.Rect;
                var dir = new Vector2(1f, 0.5f);
                var from = c.resize ? new Vector2(before.xMax - 3, before.yMax - 3) : new Vector2(before.x + 40, before.y + 6);
                if (c.resize) _events.Enqueue(Mouse(EventType.MouseDown, from, Vector2.zero));
                else { _mouse = from; _win.BeginDrag(); }
                for (int i = 1; i <= c.steps; i++) _events.Enqueue(Mouse(EventType.MouseDrag, from + dir * (c.step * i), dir * c.step));
                _events.Enqueue(Mouse(EventType.MouseUp, from + dir * (c.step * c.steps), Vector2.zero));
                while (_events.Count > 0) yield return null;
                yield return null; yield return null;
                var after = _win.Rect;
                var want = dir * (c.step * c.steps);
                var got = c.resize ? after.size - before.size : after.position - before.position;
                bool ok = Mathf.Abs(got.x - want.x) < 0.01f && Mathf.Abs(got.y - want.y) < 0.01f;
                if (!ok) _fails++;
                Log($"{(ok ? "PASS" : "FAIL")} {c.name}: mouse moved ({want.x:0.##}, {want.y:0.##}), window {(c.resize ? "grew" : "moved")} ({got.x:0.##}, {got.y:0.##})");
                _win.Close();
                yield return null;
            }
            _win = null;
            UiWindow.TestMouse = null;
            Log(_fails == 0 ? "done: all passed" : $"done: {_fails} failed");
        }
    }
}
