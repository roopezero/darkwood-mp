using System.Collections.Generic;
using DarkwoodMP.World.Skills;
using UnityEngine;

namespace DarkwoodMP.World
{
    // One skill's cell, and the game's names and descriptions.
    public static partial class SkillsPanel
    {
        // The frames' colours: a learned skill's (bright, red for a negative
        // one), the one chosen to learn, the mouse's.
        private static readonly Color LearnedFrame = new Color(
            0.86f, 0.80f, 0.62f), LearnedNegFrame = new Color(
                0.80f, 0.32f, 0.26f),
            ChosenFrame = new Color(0.62f, 0.62f, 0.62f),
            HoverFrame = new Color(0.40f, 0.40f, 0.40f);

        /// <summary>
        /// One skill: the icon centred in its cell, and the whole cell takes
        /// the mouse (the hover, the details and the click), so the space
        /// between the icons isn't dead. A learned skill is drawn in full
        /// colour in a bright frame with a check mark in its corner; one chosen
        /// to learn has a grey frame; the others are dimmed.
        /// </summary>
        private static void DrawSkill(
            Rect cell, float icon, PlayerSkill sk, bool have, bool can,
            bool picked, bool negative)
        {
            float S = UiWindow.S;
            var r = new Rect(
                cell.x + (cell.width - icon) / 2f,
                cell.y + (cell.height - icon) / 2f, icon, icon);
            Vector2 mouse;
            if (TestMouse.HasValue)
            {
                mouse = GUIUtility.ScreenToGUIPoint(TestMouse.Value);
            }
            else
            {
                mouse = Event.current.mousePosition;
            }

            bool hover = cell.Contains(mouse);
            if (hover)
            {
                _hover = sk.name;
            }

            var frame = new Rect(
                r.x - 3 * S, r.y - 3 * S, r.width + 6 * S, r.height + 6 * S);
            GUI.Box(frame, GUIContent.none, MenuSkin.Sunken);
            Color tint;
            if (have)
            {
                tint = Color.white;
            }
            else if (can)
            {
                if (hover)
                {
                    tint = new Color(0.85f, 0.85f, 0.85f);
                }
                else
                {
                    tint = new Color(0.55f, 0.55f, 0.55f);
                }
            }
            else
            {
                tint = new Color(0.25f, 0.25f, 0.25f);
            }

            if (negative)
            {
                float brightness = 0.3f;
                if (have)
                {
                    brightness = 1f;
                }
                else if (can)
                {
                    if (hover)
                    {
                        brightness = 0.9f;
                    }
                    else
                    {
                        brightness = 0.65f;
                    }
                }

                tint = new Color(1f, 0.4f, 0.4f) * brightness;
            }

            tint.a = 1f;
            if (!SkillIcons.Draw(r, sk.name, tint))
            {
                PixelFont.Draw(
                    r, PixelFont.Fit(SkillName(sk.name), r.width), tint,
                    TextAnchor.MiddleCenter);
            }

            if (Event.current.type == EventType.Repaint)
            {
                if (have)
                {
                    Color c;
                    if (negative)
                    {
                        c = LearnedNegFrame;
                    }
                    else
                    {
                        c = LearnedFrame;
                    }

                    Outline(frame, 2 * S, c);
                }
                else if (picked)
                {
                    Outline(frame, 2 * S, ChosenFrame);
                }
                else if (hover && can)
                {
                    Outline(frame, S, HoverFrame);
                }

                if (have)
                {
                    Color markColor;
                    if (negative)
                    {
                        markColor = LearnedNegFrame;
                    }
                    else
                    {
                        markColor = LearnedFrame;
                    }

                    CheckMark(new Vector2(frame.xMax, frame.y), S, markColor);
                }

                _rects[sk.name] = new[] { ToScreen(r), ToScreen(cell) };
            }

            if (GUI.Button(cell, GUIContent.none, GUIStyle.none)
                && SkillPanelRules.TakesClick(can, have))
            {
                _message = null;
                // A negative skill goes with the first skill of its own tier: a
                // pick that doesn't fit the other drops it.
                var p = Player.Instance;
                HashSet<string> chosen = null;
                if (p != null)
                {
                    chosen = SkillTree.Chosen(p);
                }

                var picks = SkillPanelRules.Pick(
                    new PickFacts
                    {
                        Tiers = SkillTree.Facts,
                        Chosen = chosen,
                        Clicked = sk.name,
                        ClickedNegative = negative,
                        Good = _good,
                        Negative = _negative,
                    });

                _good = picks.Good;
                _negative = picks.Negative;
                try
                {
                    AudioController.Play("UI_click");
                }
                catch
                {
                }
            }
        }

        private static Rect ToScreen(Rect r)
        {
            var a = GUIUtility.GUIToScreenPoint(r.min);
            return new Rect(a, r.size);
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Outline(Rect r, float t, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>
        /// A small pixel check mark on a dark square, its top right corner at
        /// this point.
        /// </summary>
        private static void CheckMark(Vector2 topRight, float S, Color c)
        {
            float u = Mathf.Max(1f, Mathf.Round(S * 1.5f)),
                w = 11 * u,
                h = 9 * u;
            var box = new Rect(topRight.x - w, topRight.y, w, h);
            Fill(box, new Color(0.06f, 0.06f, 0.06f, 1f));
            Outline(box, Mathf.Max(1f, S), c);
            // The tick: down from the left to the bottom, then up to the right,
            // two pixels thick.
            int[] ys = { 4, 5, 6, 5, 4, 3, 2 };
            for (int i = 0; i < ys.Length; i++)
            {
                Fill(
                    new Rect(
                        box.x + (2 + i) * u, box.y + (ys[i] - 1) * u, u, 2 * u),
                    c);
            }
        }

        private static readonly Dictionary<string, string> _names =
            new Dictionary<string, string>(),
            _descs = new Dictionary<string, string>();

        public static string SkillName(string name) =>
            Lang(_names, name + "_name", name);
        public static string SkillDesc(string name) =>
            Lang(_descs, name + "_desc", "");

        /// <summary>
        /// The game's text (sheet Skills), without tk2d's colour codes.
        /// </summary>
        private static string Lang(
            Dictionary<string, string> cache, string key, string fallback)
        {
            if (cache.TryGetValue(key, out var s))
            {
                return s;
            }

            try
            {
                if (global::Language.HasKey(key, "Skills"))
                {
                    s = global::Language.Get(key, "Skills");
                }
                else
                {
                    s = fallback;
                }
            }
            catch
            {
                s = fallback;
            }

            if (s == null)
            {
                s = fallback;
            }

            s = Clean(s);
            cache[key] = s;
            return s;
        }

        private static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                // tk2d inline colours: ^cRGBA (hex) or ^CRRGGBBAA.
                if (s[i] == '^' && i + 1 < s.Length
                    && (s[i + 1] == 'c' || s[i + 1] == 'C'))
                {
                    int skip = 8;
                    if (s[i + 1] == 'c')
                    {
                        skip = 4;
                    }

                    i += 1 + skip;
                    continue;
                }

                char ch;
                if (s[i] == '\r')
                {
                    ch = ' ';
                }
                else
                {
                    ch = s[i];
                }

                sb.Append(ch);
            }

            return sb.ToString().Trim();
        }
    }
}
