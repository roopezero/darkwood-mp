using System;
using System.Collections.Generic;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.Menus
{
    internal sealed partial class OptionsWindow
    {
        // ---- a dropdown list ------------------------------------------------

        private sealed class Drop
        {
            public string Key;
            public string[] Options;
            public int Index, First = -1;
            public Action<int> Pick;
            // the button's bottom left corner, on the screen
            public Vector2 Below;
            public float Top, Width;
        }

        private Drop _drop;

        /// <summary>
        /// A button showing the chosen one of <paramref name="options"/>; a
        /// click opens the list under it.
        /// </summary>
        private void Choice(
            Rect r, string key, string[] options, int index, Action<int> pick)
        {
            bool open = _drop != null && _drop.Key == key;
            bool testOpen =
                _testDropKey == key && Event.current.type == EventType.Repaint;
            if (testOpen)
            {
                _testDropKey = null;
                open = false;
            }

            if (Ui.Button(r, "") || testOpen)
            {
                if (open)
                {
                    _drop = null;
                }
                else
                {
                    _drop = new Drop
                    {
                        Key = key,
                        Options = options,
                        Index = index,
                        Pick = pick,
                        Width = r.width,
                        Below = GUIUtility.GUIToScreenPoint(
                            new Vector2(r.x, r.yMax)),
                        Top = GUIUtility
                            .GUIToScreenPoint(new Vector2(r.x, r.y)).y,
                    };
                }
            }

            string text = "";
            if (index >= 0 && index < options.Length)
            {
                text = options[index];
            }

            bool hover = r.Contains(Event.current.mousePosition);
            var textBox =
                new Rect(r.x + 6 * S, r.y, r.width - 20 * S, r.height);
            string fittedText = PixelFont.Fit(text, r.width - 22 * S);
            Color color;
            if (hover || open)
            {
                color = MenuSkin.Bright;
            }
            else
            {
                color = MenuSkin.Text;
            }

            Ui.Label(textBox, fittedText, color);
            DrawArrow(new Rect(r.xMax - 14 * S, r.y, 9 * S, r.height), color);
        }

        /// <summary>A small triangle pointing down.</summary>
        private void DrawArrow(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var old = GUI.color;
            GUI.color = c;
            float y = r.center.y - 2 * S;
            for (int i = 0; i < 4; i++)
            {
                GUI.DrawTexture(
                    new Rect(r.x + i * S, y + i * S, r.width - 2 * i * S, S),
                    MenuSkin.White);
            }

            GUI.color = old;
        }

        /// <summary>
        /// The open list, over everything in the window (UiWindow.Overlay): no
        /// IMGUI controls, events by hand.
        /// </summary>
        private void DrawDrop()
        {
            if (_drop == null)
            {
                return;
            }

            var e = Event.current;
            float rowH = PixelFont.Line + 5 * S;
            var below = GUIUtility.ScreenToGUIPoint(_drop.Below);
            float top = GUIUtility.ScreenToGUIPoint(
                new Vector2(_drop.Below.x, _drop.Top)).y;
            int n = _drop.Options.Length;
            int fitDown =
                Mathf.FloorToInt((_win.Rect.height - below.y - 8 * S) / rowH);
            int fitUp =
                Mathf.FloorToInt((top - PixelFont.Line - 12 * S) / rowH);
            bool up = fitDown < Mathf.Min(n, 6) && fitUp > fitDown;
            int fit;
            if (up)
            {
                fit = fitUp;
            }
            else
            {
                fit = fitDown;
            }

            int vis = Mathf.Clamp(fit, 1, n);
            float h = vis * rowH + 4 * S;
            float boxTop;
            if (up)
            {
                boxTop = top - h;
            }
            else
            {
                boxTop = below.y;
            }

            var box = new Rect(below.x, boxTop, _drop.Width, h);
            if (_drop.First < 0)
            {
                _drop.First = Mathf.Clamp(
                    _drop.Index - vis / 2, 0, Mathf.Max(0, n - vis));
            }

            _drop.First = Mathf.Clamp(_drop.First, 0, Mathf.Max(0, n - vis));
            int Under(Vector2 m)
            {
                if (box.Contains(m))
                {
                    return _drop.First + Mathf.FloorToInt(
                        (m.y - box.y - 2 * S) / rowH);
                }

                return -1;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    {
                        int i = Under(e.mousePosition);
                        var pick = _drop.Pick;
                        _drop = null;
                        e.Use();
                        if (i >= 0 && i < n)
                        {
                            pick(i);
                        }

                        return;
                    }

                case EventType.ScrollWheel:
                    if (box.Contains(e.mousePosition))
                    {
                        if (e.delta.y > 0)
                        {
                            _drop.First += 1;
                        }
                        else
                        {
                            _drop.First -= 1;
                        }

                        e.Use();
                    }

                    return;
                case EventType.Repaint:
                    {
                        // Opaque, so the settings under it don't show through.
                        var old = GUI.color;
                        GUI.color = new Color(0.09f, 0.09f, 0.09f, 1f);
                        GUI.DrawTexture(box, MenuSkin.White);
                        GUI.color = old;
                        GUI.Box(box, GUIContent.none, MenuSkin.Window);
                        int hover = Under(e.mousePosition);
                        for (int k = 0; k < vis && _drop.First + k < n; k++)
                        {
                            int i = _drop.First + k;
                            var rr = new Rect(
                                box.x + 2 * S, box.y + 2 * S + k * rowH,
                                box.width - 4 * S, rowH);
                            if (i == hover)
                            {
                                GUI.Box(rr, GUIContent.none, MenuSkin.Selected);
                            }

                            var optionBox = new Rect(
                                rr.x + 4 * S, rr.y, rr.width - 12 * S,
                                rr.height);
                            string fittedOption = PixelFont.Fit(
                                _drop.Options[i], rr.width - 14 * S);
                            Color optionColor;
                            if (i == hover || i == _drop.Index)
                            {
                                optionColor = MenuSkin.Bright;
                            }
                            else
                            {
                                optionColor = MenuSkin.Text;
                            }

                            Ui.Label(optionBox, fittedOption, optionColor);
                        }

                        if (vis < n)
                        {
                            // Where the shown part is in the list: a thin bar
                            // along the right edge.
                            float bh = (box.height - 4 * S) * vis / n,
                                by = box.y + 2 * S
                                    + (box.height - 4 * S) * _drop.First / n;
                            GUI.Box(
                                new Rect(box.xMax - 5 * S, by, 3 * S, bh),
                                GUIContent.none, MenuSkin.Line);
                        }

                        return;
                    }
            }
        }
    }
}
