using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The controls the co-op windows are made of, drawn with MenuSkin and
    /// PixelFont.
    /// </summary>
    public static class Ui
    {
        public static float S => PixelFont.Scale;
        public static float RowH => PixelFont.Line + 8 * S;

        public static Rect[] Split(Rect r, params float[] widths)
        {
            float gap = 4 * S, fixedW = 0;
            int flex = 0;
            foreach (var w in widths)
            {
                if (w > 0)
                {
                    fixedW += w * S;
                }
                else
                {
                    flex++;
                }
            }

            float flexW = 0;
            if (flex > 0)
            {
                flexW = Mathf.Max(
                    0, (r.width - fixedW - gap * (widths.Length - 1)) / flex);
            }

            var rects = new Rect[widths.Length];
            float x = r.x;
            for (int i = 0; i < widths.Length; i++)
            {
                float w;
                if (widths[i] > 0)
                {
                    w = widths[i] * S;
                }
                else
                {
                    w = flexW;
                }

                rects[i] = new Rect(x, r.y, w, r.height);
                x += w + gap;
            }

            return rects;
        }

        public static void Label(
            Rect r, string text, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            Color shade;
            if (color.HasValue)
            {
                shade = color.Value;
            }
            else
            {
                shade = MenuSkin.Text;
            }

            PixelFont.Draw(r, text, shade, align);
        }

        public static void Label(Flow f, string text, Color? color = null) =>
            Label(f.Next(PixelFont.Line), text, color);

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

        /// <summary>
        /// A section's heading, with a line under it, as the game's option
        /// pages.
        /// </summary>
        public static void Heading(Flow f, string text)
        {
            f.Space(4);
            var r = f.Next(PixelFont.Line + 2 * S);
            PixelFont.Draw(
                new Rect(r.x, r.y, r.width, PixelFont.Line),
                text.ToUpperInvariant(), MenuSkin.Bright, TextAnchor.MiddleLeft,
                PixelFont.Kind.Title);
            GUI.Box(
                new Rect(r.x, r.yMax - S, r.width, S), GUIContent.none,
                MenuSkin.Line);
            f.Space(2);
        }

        public static bool Button(Rect r, string text, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            bool clicked;
            if (enabled)
            {
                clicked = GUI.Button(r, GUIContent.none, MenuSkin.Button);
            }
            else
            {
                GUI.Box(r, GUIContent.none, MenuSkin.Button);
                clicked = false;
            }

            bool down = enabled && GUIUtility.hotControl != 0 && hover
                && Input.GetMouseButton(0);
            Rect tr;
            if (down)
            {
                tr = new Rect(r.x + S, r.y + S, r.width, r.height);
            }
            else
            {
                tr = r;
            }

            Color color;
            if (!enabled)
            {
                color = MenuSkin.Dim;
            }
            else if (hover)
            {
                color = MenuSkin.Bright;
            }
            else
            {
                color = MenuSkin.Text;
            }

            PixelFont.Draw(tr, text, color, TextAnchor.MiddleCenter);
            return clicked;
        }

        public static bool Button(Flow f, string text, bool enabled = true) =>
            Button(f.Next(RowH), text, enabled);

        /// <summary>
        /// A tab: raised and joined to the panel under it when chosen.
        /// </summary>
        public static bool Tab(Rect r, string text, bool chosen)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (chosen)
            {
                GUI.Box(
                    new Rect(r.x, r.y, r.width, r.height + 2 * S),
                    GUIContent.none, MenuSkin.Button);
            }

            bool clicked = !chosen
                && GUI.Button(
                    new Rect(r.x, r.y + 2 * S, r.width, r.height - 2 * S),
                    GUIContent.none, MenuSkin.Pressed);
            Rect tab;
            if (chosen)
            {
                tab = r;
            }
            else
            {
                tab = new Rect(r.x, r.y + 2 * S, r.width, r.height - 2 * S);
            }

            Color color;
            if (chosen || hover)
            {
                color = MenuSkin.Bright;
            }
            else
            {
                color = MenuSkin.Text;
            }

            PixelFont.Draw(tab, text, color, TextAnchor.MiddleCenter);
            return clicked;
        }

        public static string Field(
            Rect r, string value, int max, bool password = false,
            string name = null)
        {
            if (name != null)
            {
                GUI.SetNextControlName(name);
            }

            if (password)
            {
                string shown = value;
                if (shown == null)
                {
                    shown = "";
                }

                return GUI.PasswordField(r, shown, '*', max, MenuSkin.Field);
            }

            string text = value;
            if (text == null)
            {
                text = "";
            }

            return GUI.TextField(r, text, max, MenuSkin.Field);
        }

        /// <summary>
        /// A label on the left and a field beside it, on one row.
        /// </summary>
        public static string Field(
            Flow f, string label, string value, int max, bool password = false,
            float labelWidth = 90, string name = null)
        {
            var c = f.Row(RowH, labelWidth, 0);
            Label(c[0], label);
            return Field(c[1], value, max, password, name);
        }

        /// <summary>
        /// A check box with its label; when not enabled it can't be clicked and
        /// its label is dim.
        /// </summary>
        public static bool Check(
            Rect r, bool on, string text, bool enabled = true)
        {
            float box = PixelFont.Line - 2 * S;
            var b = new Rect(r.x, r.y + (r.height - box) / 2, box, box);
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            if (enabled && GUI.Button(r, GUIContent.none, MenuSkin.Plain))
            {
                on = !on;
            }

            GUI.Box(b, GUIContent.none, MenuSkin.Sunken);
            if (on)
            {
                GUI.Box(
                    new Rect(
                        b.x + 3 * S, b.y + 3 * S, b.width - 6 * S,
                        b.height - 6 * S),
                    GUIContent.none, MenuSkin.Line);
            }

            Color color;
            if (!enabled)
            {
                color = MenuSkin.Dim;
            }
            else if (hover)
            {
                color = MenuSkin.Bright;
            }
            else
            {
                color = MenuSkin.Text;
            }

            Label(
                new Rect(b.xMax + 6 * S, r.y, r.width - box - 6 * S, r.height),
                text, color);
            return on;
        }

        /// <summary>
        /// Enter pressed while one of these windows has the keyboard.
        /// </summary>
        public static bool EnterPressed()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Return
                    || e.keyCode == KeyCode.KeypadEnter))
            {
                e.Use();
                return true;
            }

            return false;
        }
    }
}
