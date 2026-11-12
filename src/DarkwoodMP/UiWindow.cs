using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// A window in the style of Half-Life's menus: a title bar to drag it by, a close box, a grip in the bottom right
    /// corner to resize it, and a body that scrolls when it doesn't fit. Its first place and size come from
    /// DefaultRect (in screen pixels, for the screen as it is); after that it stays where it was put, kept on the
    /// screen. A new screen size puts it back in its default place.
    /// </summary>
    public sealed class UiWindow
    {
        public readonly int Id;
        public string Title;
        public bool Open;
        public Rect Rect;
        /// <summary>The smallest size, in pixels at scale 1 (PixelFont.Scale).</summary>
        public Vector2 MinSize = new Vector2(220, 120);
        public Func<Rect> DefaultRect;
        /// <summary>Draws the body. It gets a Flow over the body's width; the body scrolls if the flow ends below it.</summary>
        public Action<Flow> Body;
        /// <summary>Drawn along the bottom, outside the scrolling part (buttons like Close); its height in pixels at scale 1.</summary>
        public Action<Rect> Footer;
        public float FooterHeight;
        public Action OnClose;
        /// <summary>Its height follows what's in it (up to the screen's) until the player resizes it.</summary>
        public bool FitContent;
        /// <summary>Drawn as a modal window (GUI.ModalWindow): on top of the others, which take no clicks while it's open.</summary>
        public bool Modal;
        /// <summary>
        /// Drawn over everything else in the window, in its own coordinates (a dropdown's open list). It gets the
        /// window's input events first, so it can take a click before the body under it does, and is painted last.
        /// It must not use IMGUI controls (their ids would move between events); it handles the events itself.
        /// </summary>
        public Action Overlay;

        private Vector2 _scroll;
        private float _contentHeight;
        private bool _scrolls;
        private Vector2Int _placedFor;
        private bool _resizing;
        private Vector2 _resizeFrom, _sizeFrom;
        private bool _dragging;
        private Vector2 _dragFrom, _placeFrom;
        private bool _focusNext;
        private bool _userSized;
        private float _fitHeight;

        public UiWindow(int id, string title) { Id = id; Title = title; }

        /// <summary>For tests: whether the body scrolls this frame, and how tall its content was.</summary>
        internal bool TestScrolls => _scrolls;
        internal float TestContentHeight => _contentHeight;

        /// <summary>
        /// A first place on the right side of the screen, clear of the menu on the left: its right edge a margin from
        /// the screen's, its top at <paramref name="top"/> of the screen's height (or centred when negative).
        /// </summary>
        public static Rect RightSide(float w, float h, float top = -1f)
        {
            w = Mathf.Min(w, Screen.width - 32); h = Mathf.Min(h, Screen.height - 32);
            float margin = Mathf.Max(16f, Screen.width * 0.04f);
            float y = top < 0f ? (Screen.height - h) / 2f : Screen.height * top;
            return new Rect(Mathf.Max(16f, Screen.width - w - margin), Mathf.Clamp(y, 16f, Mathf.Max(16f, Screen.height - h - 16f)), w, h);
        }

        public static float S => PixelFont.Scale;
        private static float TitleH => (PixelFont.Line + 8 * S);
        private static float Pad => 8 * S;
        private static float Grip => 12 * S;

        public void Show() { Open = true; _focusNext = true; _dragging = false; _resizing = false; }

        public void Close()
        {
            if (!Open) return;
            Open = false;
            OnClose?.Invoke();
        }

        /// <summary>Back to its default place the next time it's drawn.</summary>
        public void ResetPlace() => _placedFor = default;

        public void OnGUI()
        {
            if (!Open) return;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (_placedFor != screen) { Rect = DefaultRect(); _placedFor = screen; _scroll = Vector2.zero; _userSized = false; _fitHeight = 0; }
            if (FitContent && !_userSized && _fitHeight > 0 && Event.current.type == EventType.Layout)
                Rect.height = Mathf.Min(_fitHeight, Screen.height - 16 * S);
            HandleResize();
            HandleDrag();
            Rect = Modal ? GUI.ModalWindow(Id, Rect, Draw, GUIContent.none, MenuSkin.Window) : GUI.Window(Id, Rect, Draw, GUIContent.none, MenuSkin.Window);
            Clamp();
            if (_focusNext) { _focusNext = false; GUI.FocusWindow(Id); GUI.BringWindowToFront(Id); }
        }

        /// <summary>
        /// Where the mouse is, in screen pixels with y down (only differences between two of these are used). Moves
        /// and resizes follow this every frame instead of the GUI's MouseDrag events: with a real mouse, Unity sends
        /// none for movements of a pixel or two (measured with --dwmp-test-drag-real: 100 slow 1 px steps gave no
        /// event at all, then the window jumped when the mouse went faster), which is what made slow drags stick.
        /// DragTest puts its made-up mouse here.
        /// </summary>
        internal static Func<Vector2> TestMouse;
        private static Vector2 MouseNow() => TestMouse != null ? TestMouse() : new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        /// <summary>The button let go somewhere the window didn't get the MouseUp (outside the game's window, say).</summary>
        private static bool Released() => TestMouse == null && !Input.GetMouseButton(0);

        /// <summary>The grip is handled before the window takes the event, in screen pixels, so a drag that leaves the window still resizes it.</summary>
        private void HandleResize()
        {
            var e = Event.current;
            var grip = new Rect(Rect.xMax - Grip, Rect.yMax - Grip, Grip, Grip);
            if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                _resizing = true; _userSized = true; _resizeFrom = MouseNow(); _sizeFrom = Rect.size;
                e.Use();
                return;
            }
            if (!_resizing) return;
            if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp || Released()) { _resizing = false; if (e.isMouse) e.Use(); return; }
            var size = _sizeFrom + (MouseNow() - _resizeFrom);
            Rect.width = Mathf.Clamp(size.x, MinSize.x * S, Screen.width - Rect.x);
            Rect.height = Mathf.Clamp(size.y, MinSize.y * S, Screen.height - Rect.y);
            if (e.type == EventType.MouseDrag) e.Use();
        }

        /// <summary>
        /// Moving by the title bar. The drag starts in Draw (so a window on top of this one takes its own clicks) and is
        /// followed here, before the window takes the event, on every event of every frame. The place is always the
        /// place at the start plus how far the mouse is from where it was then, so no movement is lost however slowly
        /// it moves. It replaces Unity's GUI.DragWindow, with which slow drags felt stuck (issue #18). DragTest checks it.
        /// </summary>
        internal void BeginDrag() { _dragging = true; _placeFrom = Rect.position; _dragFrom = MouseNow(); }

        private void HandleDrag()
        {
            var e = Event.current;
            if (!_dragging) return;
            if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp || Released()) { _dragging = false; if (e.isMouse) e.Use(); return; }
            Rect.position = _placeFrom + (MouseNow() - _dragFrom);
            if (e.type == EventType.MouseDrag) e.Use();
        }

        private void Clamp()
        {
            Rect.width = Mathf.Clamp(Rect.width, Mathf.Min(MinSize.x * S, Screen.width), Screen.width);
            Rect.height = Mathf.Clamp(Rect.height, Mathf.Min(MinSize.y * S, Screen.height), Screen.height);
            Rect.x = Mathf.Clamp(Rect.x, 0, Screen.width - Rect.width);
            Rect.y = Mathf.Clamp(Rect.y, 0, Screen.height - Rect.height);
        }

        private void Draw(int id)
        {
            float w = Rect.width, h = Rect.height;
            if (Overlay != null && Event.current.type != EventType.Repaint) Overlay();
            // Title bar, with the close box at its right end.
            var bar = new Rect(2, 2, w - 4, TitleH);
            GUI.Box(bar, GUIContent.none, MenuSkin.TitleBar);
            PixelFont.Draw(new Rect(bar.x + Pad, bar.y, bar.width - 2 * Pad, bar.height), Title.ToUpperInvariant(), MenuSkin.Bright, TextAnchor.MiddleLeft, PixelFont.Kind.Title);
            float cb = TitleH - 8 * S;
            if (Ui.Button(new Rect(bar.xMax - cb - 4 * S, bar.y + 4 * S, cb, cb), "x")) Close();
            if (!Open) return;
            // A press on the title bar (the close box has taken its own) starts a move, followed in HandleDrag.
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && new Rect(0, 0, w, TitleH + 2).Contains(ev.mousePosition))
            {
                BeginDrag();
                ev.Use();
            }

            float footer = Footer != null ? FooterHeight * S : 0f;
            var body = new Rect(Pad, bar.yMax + Pad, w - 2 * Pad, h - bar.yMax - 2 * Pad - (footer > 0 ? footer + Pad : 0));
            // Whether it scrolls is decided once a frame, on Layout, so the Repaint after it can't differ: a scrollbar
            // that came and went within a frame narrowed the body, re-wrapped its text and pushed everything aside.
            if (Event.current.type == EventType.Layout) _scrolls = _contentHeight > body.height + 1;
            bool scrolls = _scrolls;
            float inner = body.width - (scrolls ? 12 * S : 0);
            _scroll = GUI.BeginScrollView(body, _scroll, new Rect(0, 0, inner, Mathf.Max(_contentHeight, body.height)), false, false, GUIStyle.none, scrolls ? GUI.skin.verticalScrollbar : GUIStyle.none);
            var flow = new Flow(new Rect(0, 0, inner, body.height));
            Body(flow);
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint)
            {
                _contentHeight = flow.Y;
                _fitHeight = bar.yMax + 2 * Pad + flow.Y + (footer > 0 ? footer + Pad : 0) + 2;
            }
            GUI.EndScrollView();
            if (footer > 0) Footer(new Rect(Pad, h - Pad - footer, w - 2 * Pad - Grip * 0.5f, footer));

            // The grip: three short diagonal lines.
            if (Event.current.type == EventType.Repaint)
            {
                for (int i = 1; i <= 3; i++)
                {
                    float o = i * 3 * S;
                    for (int j = 0; j < (int)o; j += (int)S)
                        GUI.DrawTexture(new Rect(w - 3 * S - o + j, h - 3 * S - j - S, S, S), MenuSkin.Line.normal.background);
                }
            }
            if (Overlay != null && Event.current.type == EventType.Repaint) Overlay();
        }
    }

    /// <summary>A top-to-bottom layout for a window's body: each call takes the next strip of it.</summary>
    public sealed class Flow
    {
        public readonly Rect Area;
        public float Y;
        public float Gap => 4 * UiWindow.S;

        public Flow(Rect area) { Area = area; Y = area.y; }

        public float Width => Area.width;

        /// <summary>The next strip, h pixels (already scaled) high.</summary>
        public Rect Next(float h)
        {
            var r = new Rect(Area.x, Y, Area.width, h);
            Y += h + Gap;
            return r;
        }

        public void Space(float unscaled) => Y += unscaled * UiWindow.S;

        /// <summary>
        /// The next strip cut into columns: a width above 0 is fixed (pixels at scale 1), 0 or below shares what's left.
        /// </summary>
        public Rect[] Row(float h, params float[] widths) => Ui.Split(Next(h), widths);
    }

    /// <summary>The controls the co-op windows are made of, drawn with MenuSkin and PixelFont.</summary>
    public static class Ui
    {
        public static float S => PixelFont.Scale;
        public static float RowH => PixelFont.Line + 8 * S;

        public static Rect[] Split(Rect r, params float[] widths)
        {
            float gap = 4 * S, fixedW = 0; int flex = 0;
            foreach (var w in widths) { if (w > 0) fixedW += w * S; else flex++; }
            float flexW = flex > 0 ? Mathf.Max(0, (r.width - fixedW - gap * (widths.Length - 1)) / flex) : 0;
            var rects = new Rect[widths.Length];
            float x = r.x;
            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i] > 0 ? widths[i] * S : flexW;
                rects[i] = new Rect(x, r.y, w, r.height);
                x += w + gap;
            }
            return rects;
        }

        public static void Label(Rect r, string text, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft) =>
            PixelFont.Draw(r, text, color ?? MenuSkin.Text, align);

        public static void Label(Flow f, string text, Color? color = null) => Label(f.Next(PixelFont.Line), text, color);

        /// <summary>Text wrapped to the flow's width.</summary>
        public static void Text(Flow f, string text, Color? color = null)
        {
            foreach (var line in PixelFont.Wrap(text, f.Width))
            {
                var r = new Rect(f.Area.x, f.Y, f.Width, PixelFont.Line);
                Label(r, line, color);
                f.Y += PixelFont.Line + S;
            }
            f.Y += f.Gap - S;
        }

        /// <summary>A section's heading, with a line under it, as the game's option pages.</summary>
        public static void Heading(Flow f, string text)
        {
            f.Space(4);
            var r = f.Next(PixelFont.Line + 2 * S);
            PixelFont.Draw(new Rect(r.x, r.y, r.width, PixelFont.Line), text.ToUpperInvariant(), MenuSkin.Bright, TextAnchor.MiddleLeft, PixelFont.Kind.Title);
            GUI.Box(new Rect(r.x, r.yMax - S, r.width, S), GUIContent.none, MenuSkin.Line);
            f.Space(2);
        }

        public static bool Button(Rect r, string text, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            bool clicked;
            if (enabled) clicked = GUI.Button(r, GUIContent.none, MenuSkin.Button);
            else { GUI.Box(r, GUIContent.none, MenuSkin.Button); clicked = false; }
            bool down = enabled && GUIUtility.hotControl != 0 && hover && Input.GetMouseButton(0);
            var tr = down ? new Rect(r.x + S, r.y + S, r.width, r.height) : r;
            PixelFont.Draw(tr, text, !enabled ? MenuSkin.Dim : hover ? MenuSkin.Bright : MenuSkin.Text, TextAnchor.MiddleCenter);
            return clicked;
        }

        public static bool Button(Flow f, string text, bool enabled = true) => Button(f.Next(RowH), text, enabled);

        /// <summary>A tab: raised and joined to the panel under it when chosen.</summary>
        public static bool Tab(Rect r, string text, bool chosen)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (chosen) { GUI.Box(new Rect(r.x, r.y, r.width, r.height + 2 * S), GUIContent.none, MenuSkin.Button); }
            bool clicked = !chosen && GUI.Button(new Rect(r.x, r.y + 2 * S, r.width, r.height - 2 * S), GUIContent.none, MenuSkin.Pressed);
            PixelFont.Draw(chosen ? r : new Rect(r.x, r.y + 2 * S, r.width, r.height - 2 * S), text, chosen || hover ? MenuSkin.Bright : MenuSkin.Text, TextAnchor.MiddleCenter);
            return clicked;
        }

        public static string Field(Rect r, string value, int max, bool password = false, string name = null)
        {
            if (name != null) GUI.SetNextControlName(name);
            return password ? GUI.PasswordField(r, value ?? "", '*', max, MenuSkin.Field) : GUI.TextField(r, value ?? "", max, MenuSkin.Field);
        }

        /// <summary>A label on the left and a field beside it, on one row.</summary>
        public static string Field(Flow f, string label, string value, int max, bool password = false, float labelWidth = 90, string name = null)
        {
            var c = f.Row(RowH, labelWidth, 0);
            Label(c[0], label);
            return Field(c[1], value, max, password, name);
        }

        public static bool Check(Rect r, bool on, string text)
        {
            float box = PixelFont.Line - 2 * S;
            var b = new Rect(r.x, r.y + (r.height - box) / 2, box, box);
            bool hover = r.Contains(Event.current.mousePosition);
            if (GUI.Button(r, GUIContent.none, MenuSkin.Plain)) on = !on;
            GUI.Box(b, GUIContent.none, MenuSkin.Sunken);
            if (on) GUI.Box(new Rect(b.x + 3 * S, b.y + 3 * S, b.width - 6 * S, b.height - 6 * S), GUIContent.none, MenuSkin.Line);
            Label(new Rect(b.xMax + 6 * S, r.y, r.width - box - 6 * S, r.height), text, hover ? MenuSkin.Bright : MenuSkin.Text);
            return on;
        }

        /// <summary>Enter pressed while one of these windows has the keyboard.</summary>
        public static bool EnterPressed()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { e.Use(); return true; }
            return false;
        }
    }
}
