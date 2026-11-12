using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// Text in the game's own pixel font (the 11-pixel Tahoma its menus use) for the co-op windows. The game's fonts
    /// are tk2d bitmap fonts, which IMGUI can't use, so each letter is drawn as a piece of the font's texture, at a
    /// whole-number scale so the pixels stay sharp: 1 below 1000 lines, 2 above. Until the menu has loaded the fonts,
    /// or for a letter a font hasn't got, Unity's own font is used.
    /// </summary>
    public static class PixelFont
    {
        public enum Kind { Body, Title }

        private sealed class Face
        {
            public tk2dFontData Data;
            public Texture Texture;
            public float LineHeight;
            public float CapTop, CapHeight; // where a capital H's top is (y up) and how tall it is, in font pixels
        }

        private static Face _body, _title;
        private static float _nextLook;
        private static GUIStyle _fallback;

        public static int Scale => Screen.height >= 1000 ? 2 : 1;

        /// <summary>The height of a line of text, in screen pixels.</summary>
        public static float Line => 13f * Scale;

        private static Face Get(Kind kind)
        {
            if ((_body == null || _title == null) && Time.unscaledTime >= _nextLook)
            {
                _nextLook = Time.unscaledTime + 1f;
                foreach (var f in Resources.FindObjectsOfTypeAll<tk2dFontData>())
                {
                    if (f == null) continue;
                    var d = f.inst;
                    var tex = d?.material != null ? d.material.mainTexture : null;
                    if (tex == null) continue;
                    var face = new Face { Data = d, Texture = tex, LineHeight = d.lineHeight, CapTop = 0f, CapHeight = 8f };
                    var h = Glyph(face, 'H');
                    if (h != null && h.p0.y > h.p1.y) { face.CapTop = h.p0.y; face.CapHeight = h.p0.y - h.p1.y; }
                    if (_body == null && f.name.StartsWith("tahoma_11px_light_AA") && !f.name.Contains("occluded")) _body = face;
                    if (_title == null && f.name.StartsWith("tahoma_11px_bevel_white")) _title = face;
                }
                if (_body != null && _title != null)
                    Plugin.Log.LogInfo($"Co-op windows use the game's fonts: line {_body.LineHeight}, H {_body.CapTop}/{_body.CapHeight}");
            }
            return kind == Kind.Title ? _title ?? _body : _body ?? _title;
        }

        private static tk2dFontChar Glyph(Face f, char c)
        {
            var d = f.Data;
            if (d.useDictionary)
            {
                if (d.charDict == null) d.InitDictionary();
                return d.charDict != null && d.charDict.TryGetValue(c, out var g) ? g : null;
            }
            return d.chars != null && c < d.chars.Length ? d.chars[c] : null;
        }

        private static bool CanDraw(Face f, string s)
        {
            if (f == null) return false;
            foreach (char c in s)
            {
                if (c == ' ') continue;
                var g = Glyph(f, c);
                if (g == null || g.flipped || (g.advance <= 0f && g.p1.x <= g.p0.x)) return false;
            }
            return true;
        }

        private static GUIStyle Fallback(int scale)
        {
            if (_fallback == null) _fallback = new GUIStyle(GUI.skin.label) { padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), wordWrap = false, clipping = TextClipping.Clip };
            _fallback.font = MenuSkin.FieldFont;
            _fallback.fontSize = 11 * scale;
            return _fallback;
        }

        /// <summary>The height of a line of text at this many screen pixels per font pixel.</summary>
        public static float LineAt(int scale) => 13f * scale;

        /// <summary>The text's width in screen pixels; <paramref name="scale"/> above 0 draws it bigger than Scale (the main menu's entries).</summary>
        public static float Width(string s, Kind kind = Kind.Body, int scale = 0)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            int sc = scale > 0 ? scale : Scale;
            var f = Get(kind);
            if (!CanDraw(f, s)) return Fallback(sc).CalcSize(new GUIContent(s)).x;
            float w = 0f;
            foreach (char c in s)
            {
                var g = Glyph(f, c);
                w += g != null ? g.advance : 3f;
            }
            return w * sc;
        }

        /// <summary>Draws one line of text in rect r, aligned as given, vertically centred (at Scale, or at <paramref name="scale"/> if above 0).</summary>
        public static void Draw(Rect r, string s, Color color, TextAnchor align = TextAnchor.MiddleLeft, Kind kind = Kind.Body, int scale = 0)
        {
            if (string.IsNullOrEmpty(s) || Event.current.type != EventType.Repaint) return;
            int sc = scale > 0 ? scale : Scale;
            float Line = LineAt(sc);
            float w = Width(s, kind, sc);
            float x = r.x;
            if (align == TextAnchor.MiddleCenter || align == TextAnchor.UpperCenter || align == TextAnchor.LowerCenter) x = r.x + (r.width - w) / 2f;
            else if (align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight) x = r.xMax - w;
            float top = Mathf.Round(r.y + (r.height - Line) / 2f);
            x = Mathf.Round(x);
            var f = Get(kind);
            var old = GUI.color;
            GUI.color = color * old;
            if (!CanDraw(f, s))
            {
                var fb = Fallback(sc);
                fb.normal.textColor = Color.white;
                GUI.Label(new Rect(x, top - sc, w + 4, Line + 2 * sc), s, fb);
                GUI.color = old;
                return;
            }
            // tk2d gives each letter's corners from the pen (y up). A capital letter is put in the middle of Line,
            // a pixel high, so descenders (g, p, y) still fit under it.
            float baseTop = top + Mathf.Floor((Line - f.CapHeight * sc) / 2f) - sc + f.CapTop * sc;
            foreach (char c in s)
            {
                var g = Glyph(f, c);
                if (g == null) { x += 3 * sc; continue; }
                float gw = (g.p1.x - g.p0.x) * sc, gh = (g.p0.y - g.p1.y) * sc;
                if (gw > 0f && gh > 0f)
                {
                    var gr = new Rect(x + g.p0.x * sc, baseTop - g.p0.y * sc, gw, gh);
                    if (gr.xMax > r.x - 1 && gr.x < r.xMax + 1)
                        GUI.DrawTextureWithTexCoords(gr, f.Texture, new Rect(g.uv0.x, g.uv1.y, g.uv1.x - g.uv0.x, g.uv0.y - g.uv1.y), true);
                }
                x += g.advance * sc;
            }
            GUI.color = old;
        }

        /// <summary>The text cut short with "..." to fit width.</summary>
        public static string Fit(string s, float width, Kind kind = Kind.Body)
        {
            if (string.IsNullOrEmpty(s) || Width(s, kind) <= width) return s;
            for (int n = s.Length - 1; n > 0; n--)
            {
                string t = s.Substring(0, n).TrimEnd() + "...";
                if (Width(t, kind) <= width) return t;
            }
            return "";
        }

        /// <summary>Splits text into lines no wider than width (at spaces; a word longer than a line stays whole).</summary>
        public static List<string> Wrap(string text, float width, Kind kind = Kind.Body)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? "").Split('\n'))
            {
                string line = "";
                foreach (var word in para.Split(' '))
                {
                    string tryLine = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Width(tryLine, kind) > width) { lines.Add(line); line = word; }
                    else line = tryLine;
                }
                lines.Add(line);
            }
            return lines;
        }
    }
}
