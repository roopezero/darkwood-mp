using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// Text in the game's own pixel font (the 11-pixel Tahoma its menus use)
    /// for the co-op windows. The game's fonts are tk2d bitmap fonts, which
    /// IMGUI can't use, so each letter is drawn as a piece of the font's
    /// texture, at a whole-number scale so the pixels stay sharp: 1 below 1000
    /// lines, 2 above. Until the menu has loaded the fonts, or for a letter a
    /// font hasn't got, Unity's own font is used.
    /// </summary>
    public static class PixelFont
    {
        public enum Kind
        {
            Body,
            Title
        }


        public static int Scale
        {
            get
            {
                if (Screen.height >= 1000)
                {
                    return 2;
                }

                return 1;
            }
        }

        /// <summary>The height of a line of text, in screen pixels.</summary>
        public static float Line => LineAt(Scale);

        private static FontFaces.Face Get(Kind kind)
        {
            return Plugin.Instance.Host.Fonts.Get(kind);
        }


        internal static tk2dFontChar Glyph(FontFaces.Face f, char c)
        {
            var d = f.Data;
            if (d.useDictionary)
            {
                if (d.charDict == null)
                {
                    d.InitDictionary();
                }

                if (d.charDict != null && d.charDict.TryGetValue(c, out var g))
                {
                    return g;
                }

                return null;
            }

            if (d.chars != null && c < d.chars.Length)
            {
                return d.chars[c];
            }

            return null;
        }

        private static bool CanDraw(FontFaces.Face f, string s)
        {
            if (f == null)
            {
                return false;
            }

            foreach (char c in s)
            {
                if (c == ' ')
                {
                    continue;
                }

                var g = Glyph(f, c);
                if (g == null || g.flipped
                    || (g.advance <= 0f && g.p1.x <= g.p0.x))
                {
                    return false;
                }
            }

            return true;
        }

        private static GUIStyle Fallback(int scale)
        {
            return Plugin.Instance.Host.Fonts.Fallback(scale);
        }

        /// <summary>
        /// The height of a line of text at this many screen pixels per font
        /// pixel.
        /// </summary>
        public static float LineAt(int scale) => 13f * scale;

        /// <summary>
        /// The text's width in screen pixels; <paramref name="scale"/> above 0
        /// draws it bigger than Scale (the main menu's entries).
        /// </summary>
        public static float Width(
            string s, Kind kind = Kind.Body, int scale = 0)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0f;
            }

            int sc;
            if (scale > 0)
            {
                sc = scale;
            }
            else
            {
                sc = Scale;
            }

            var f = Get(kind);
            if (!CanDraw(f, s))
            {
                return Fallback(sc).CalcSize(new GUIContent(s)).x;
            }

            float w = 0f;
            foreach (char c in s)
            {
                var g = Glyph(f, c);
                if (g != null)
                {
                    w += g.advance;
                }
                else
                {
                    w += 3f;
                }
            }

            return w * sc;
        }

        /// <summary>
        /// Draws one line of text in rect r, aligned as given, vertically
        /// centred (at Scale, or at <paramref name="scale"/> if above 0).
        /// </summary>
        public static void Draw(
            Rect r, string s, Color color,
            TextAnchor align = TextAnchor.MiddleLeft, Kind kind = Kind.Body,
            int scale = 0)
        {
            if (string.IsNullOrEmpty(s)
                || Event.current.type != EventType.Repaint)
            {
                return;
            }

            int sc;
            if (scale > 0)
            {
                sc = scale;
            }
            else
            {
                sc = Scale;
            }

            float lineH = LineAt(sc);
            float w = Width(s, kind, sc);
            float x = r.x;
            if (align == TextAnchor.MiddleCenter
                || align == TextAnchor.UpperCenter
                || align == TextAnchor.LowerCenter)
            {
                x = r.x + (r.width - w) / 2f;
            }
            else if (align == TextAnchor.MiddleRight
                || align == TextAnchor.UpperRight
                || align == TextAnchor.LowerRight)
            {
                x = r.xMax - w;
            }

            float top = Mathf.Round(r.y + (r.height - lineH) / 2f);
            x = Mathf.Round(x);
            var f = Get(kind);
            var old = GUI.color;
            GUI.color = color * old;
            if (!CanDraw(f, s))
            {
                var fb = Fallback(sc);
                fb.normal.textColor = Color.white;
                GUI.Label(new Rect(x, top - sc, w + 4, lineH + 2 * sc), s, fb);
                GUI.color = old;
                return;
            }

            // tk2d gives each letter's corners from the pen (y up). A capital
            // letter is put in the middle of the line, a pixel high, so
            // descenders (g, p, y) still fit under it.
            float baseTop = top + Mathf.Floor((lineH - f.CapHeight * sc) / 2f)
                - sc + f.CapTop * sc;
            foreach (char c in s)
            {
                var g = Glyph(f, c);
                if (g == null)
                {
                    x += 3 * sc;
                    continue;
                }

                float gw = (g.p1.x - g.p0.x) * sc, gh = (g.p0.y - g.p1.y) * sc;
                if (gw > 0f && gh > 0f)
                {
                    var gr = new Rect(
                        x + g.p0.x * sc, baseTop - g.p0.y * sc, gw, gh);
                    if (gr.xMax > r.x - 1 && gr.x < r.xMax + 1)
                    {
                        GUI.DrawTextureWithTexCoords(
                            gr, f.Texture,
                            new Rect(
                                g.uv0.x, g.uv1.y, g.uv1.x - g.uv0.x,
                                g.uv0.y - g.uv1.y),
                            true);
                    }
                }

                x += g.advance * sc;
            }

            GUI.color = old;
        }

        /// <summary>The text cut short with "..." to fit width.</summary>
        public static string Fit(string s, float width, Kind kind = Kind.Body)
        {
            if (string.IsNullOrEmpty(s) || Width(s, kind) <= width)
            {
                return s;
            }

            for (int n = s.Length - 1; n > 0; n--)
            {
                string t = s.Substring(0, n).TrimEnd() + "...";
                if (Width(t, kind) <= width)
                {
                    return t;
                }
            }

            return "";
        }

        /// <summary>
        /// Splits text into lines no wider than width (at spaces; a word longer
        /// than a line stays whole).
        /// </summary>
        public static List<string> Wrap(
            string text, float width, Kind kind = Kind.Body)
        {
            var lines = new List<string>();
            string content = text;
            if (content == null)
            {
                content = "";
            }

            foreach (var para in content.Split('\n'))
            {
                string line = "";
                foreach (var word in para.Split(' '))
                {
                    string tryLine;
                    if (line.Length == 0)
                    {
                        tryLine = word;
                    }
                    else
                    {
                        tryLine = line + " " + word;
                    }

                    if (line.Length > 0 && Width(tryLine, kind) > width)
                    {
                        lines.Add(line);
                        line = word;
                    }
                    else
                    {
                        line = tryLine;
                    }
                }

                lines.Add(line);
            }

            return lines;
        }
    }
}
