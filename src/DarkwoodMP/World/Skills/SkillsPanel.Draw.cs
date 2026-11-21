using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.World
{
    // The panel's body and footer.
    public static partial class SkillsPanel
    {
        // ---- drawing --------------------------------------------------------

        private static float Icon => 40 * UiWindow.S;

        private static void Draw(Flow f)
        {
            var p = Player.Instance;
            var tiers = SkillTree.Tiers;
            if (p == null || tiers == null)
            {
                Ui.Text(f, "No skills here.");
                return;
            }

            var chosen = SkillTree.Chosen(p);
            int level = p.currentLevel,
                points = SkillTree.Points(level, chosen);
            bool needNeg =
                _good != null && SkillTree.NeedsNegative(chosen, _good);
            if (Event.current.type == EventType.Layout)
            {
                _hover = null;
            }

            var top = f.Row(PixelFont.Line + 4 * UiWindow.S, 0, 0);
            Color color;
            if (points > 0)
            {
                color = MenuSkin.Bright;
            }
            else
            {
                color = MenuSkin.Text;
            }

            Ui.Label(top[0], $"Skill points: {points}", color);
            Ui.Label(
                top[1], $"Level {level}", MenuSkin.Dim, TextAnchor.MiddleRight);
            f.Space(4);

            // The four tiers side by side, as the game's skill choice shows
            // them: good skills, then the negative ones.
            int n = tiers.Count, rows = 0, negRows = 0;
            foreach (var t in tiers)
            {
                rows = Math.Max(rows, t.skills.Count);
                negRows = Math.Max(negRows, t.negativeSkills.Count);
            }

            float colW = f.Width / Math.Max(1, n),
                icon = Mathf.Min(Icon, colW - 8 * UiWindow.S),
                gap = 6 * UiWindow.S;
            var head = f.Next(PixelFont.Line * 2 + 2 * UiWindow.S);
            for (int i = 0; i < n; i++)
            {
                var t = tiers[i];
                bool open = t.level <= level;
                var c =
                    new Rect(head.x + i * colW, head.y, colW, PixelFont.Line);
                string tier = SkillTree.TierName(i);
                Color tierColor;
                if (open)
                {
                    tierColor = MenuSkin.Bright;
                }
                else
                {
                    tierColor = MenuSkin.Dim;
                }

                PixelFont.Draw(
                    c, tier, tierColor, TextAnchor.MiddleCenter,
                    PixelFont.Kind.Title);
                var under = new Rect(
                    c.x, c.yMax + 2 * UiWindow.S, colW, PixelFont.Line);
                string opens;
                if (open)
                {
                    opens = "open";
                }
                else
                {
                    opens = $"level {t.level}";
                }

                Ui.Label(under, opens, MenuSkin.Dim, TextAnchor.MiddleCenter);
            }

            f.Space(2);
            var grid = f.Next(rows * (icon + gap));
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < tiers[i].skills.Count; k++)
                {
                    var sk = tiers[i].skills[k];
                    if (sk == null)
                    {
                        continue;
                    }

                    var cell = new Rect(
                        grid.x + i * colW, grid.y + k * (icon + gap), colW,
                        icon + gap);
                    bool can =
                        SkillTree.CantTake(level, chosen, sk.name) == null;
                    DrawSkill(
                        cell, icon, sk, chosen.Contains(sk.name), can,
                        sk.name == _good, false);
                }
            }

            if (negRows > 0)
            {
                var lr = f.Next(PixelFont.Line + 4 * UiWindow.S);
                GUI.Box(
                    new Rect(lr.x, lr.y + lr.height / 2f, lr.width, UiWindow.S),
                    GUIContent.none, MenuSkin.Line);
                float lw = PixelFont.Width("Negative") + 12 * UiWindow.S;
                GUI.Box(
                    new Rect(lr.x + (lr.width - lw) / 2f, lr.y, lw, lr.height),
                    GUIContent.none, MenuSkin.Plain);
                Ui.Label(
                    lr, "Negative", MenuSkin.Warn, TextAnchor.MiddleCenter);
                var ngrid = f.Next(negRows * (icon + gap));
                for (int i = 0; i < n; i++)
                {
                    for (int k = 0; k < tiers[i].negativeSkills.Count; k++)
                    {
                        var sk = tiers[i].negativeSkills[k];
                        if (sk == null)
                        {
                            continue;
                        }

                        var cell = new Rect(
                            ngrid.x + i * colW, ngrid.y + k * (icon + gap),
                            colW, icon + gap);
                        // With a skill chosen, the negative skills of its tier
                        // when it needs one; else any a tier still owes.
                        bool can = (_good == null || needNeg)
                            && SkillTree.CantTakeNegative(
                                level, chosen, sk.name, _good) == null;
                        DrawSkill(
                            cell, icon, sk, chosen.Contains(sk.name), can,
                            sk.name == _negative, true);
                    }
                }
            }

            // What the skill under the mouse (or the one chosen) does.
            f.Space(2);
            GUI.Box(f.Next(UiWindow.S), GUIContent.none, MenuSkin.Line);
            f.Space(2);
            // Room for the longest description, so the window keeps its size
            // whichever skill is shown.
            MeasureDetails(tiers, f.Width);
            float detailTop = f.Y;
            string show = _hover;
            if (show == null)
            {
                show = _good;
            }

            if (show == null)
            {
                show = _negative;
            }

            if (show != null)
            {
                SkillTree.TierOf(show, out bool neg);
                Rect line = f.Next(PixelFont.Line + 2 * UiWindow.S);
                string title = SkillName(show);
                Color nameColor;
                if (neg)
                {
                    nameColor = MenuSkin.Warn;
                }
                else
                {
                    nameColor = MenuSkin.Bright;
                }

                PixelFont.Draw(
                    line, title, nameColor, TextAnchor.MiddleLeft,
                    PixelFont.Kind.Title);
                string state;
                if (chosen.Contains(show))
                {
                    state = "Learned";
                }
                else if (neg)
                {
                    state = SkillTree.CantTakeNegative(level, chosen, show);
                    if (state == null)
                    {
                        state = "Taken with the first skill of its tier";
                    }
                }
                else
                {
                    state = SkillTree.CantTake(level, chosen, show);
                    if (state == null)
                    {
                        if (SkillTree.NeedsNegative(chosen, show))
                        {
                            state = "Comes with a negative skill of its tier";
                        }
                        else
                        {
                            state = "Can be learned";
                        }
                    }
                }

                Ui.Label(f, Capital(state), MenuSkin.Dim);
                Ui.Text(f, SkillDesc(show));
            }
            else
            {
                string text = "Take a level at a stove for a skill point.";
                if (points > 0)
                {
                    text = "Choose a skill to learn.";
                }

                Ui.Text(f, text, MenuSkin.Dim);
            }

            // Never shorter than what was drawn, so a description that takes
            // more room than measured can't make the body grow while hovering.
            if (f.Y - detailTop > _detailH)
            {
                _detailH = f.Y - detailTop;
            }

            f.Y = Mathf.Max(f.Y, detailTop + _detailH);
        }

        /// <summary>
        /// Sets _detailH: the details' height for the skill with the longest
        /// description, at this width.
        /// </summary>
        private static void MeasureDetails(
            List<SkillTiers.Tier> tiers, float width)
        {
            if (Mathf.Abs(width - _detailW) < 0.5f)
            {
                return;
            }

            int lines = 2;
            foreach (var t in tiers)
            {
                foreach (var list in new[] { t.skills, t.negativeSkills })
                {
                    foreach (var sk in list)
                    {
                        if (sk != null)
                        {
                            lines = Math.Max(
                                lines,
                                PixelFont
                                    .Wrap(SkillDesc(sk.name), width).Count);
                        }
                    }
                }
            }

            float S = UiWindow.S, gap = 4 * S;   // Flow.Gap
            _detailW = width;
            // As Draw lays them out: the name (Line + 2S and a gap), the state
            // (Line and a gap), the text (Ui.Text).
            _detailH = (PixelFont.Line + 2 * S + gap) + (PixelFont.Line + gap)
                + lines * (PixelFont.Line + S) + gap - S;
        }

        private static void DrawFooter(Rect r)
        {
            var p = Player.Instance;
            if (p == null)
            {
                return;
            }

            var chosen = SkillTree.Chosen(p);
            int level = p.currentLevel;
            bool needNeg =
                _good != null && SkillTree.NeedsNegative(chosen, _good);
            var c = Ui.Split(r, 0, 90);
            string status = _message;
            if (status == null)
            {
                if (SkillTree.Waiting)
                {
                    status = "Asking the host...";
                }
                else if (_good == null)
                {
                    if (SkillTree.Points(level, chosen) > 0)
                    {
                        status = "";
                    }
                    else
                    {
                        status = "No skill points to spend";
                    }
                }
                else if (needNeg && _negative == null)
                {
                    status =
                        "Choose a negative skill of its tier to go with it";
                }
                else
                {
                    status = "";
                }
            }

            Rect cell = c[0];
            string fitted = PixelFont.Fit(status, c[0].width);
            Color tint;
            if (_message != null)
            {
                tint = MenuSkin.Warn;
            }
            else
            {
                tint = MenuSkin.Dim;
            }

            Ui.Label(cell, fitted, tint);
            bool ready = false;
            if (_good != null && !SkillTree.Waiting)
            {
                string negative = null;
                if (needNeg)
                {
                    negative = _negative;
                }

                ready = SkillTree.Check(level, chosen, _good, negative) == null;
            }

            if (Event.current.type == EventType.Repaint)
            {
                _learnRect = ToScreen(c[1]);
            }

            if (Ui.Button(c[1], "Learn", ready))
            {
                LearnChosen();
            }
        }

        private static void LearnChosen()
        {
            var p = Player.Instance;
            bool needNeg = p != null && _good != null
                && SkillTree.NeedsNegative(SkillTree.Chosen(p), _good);
            string negative = null;
            if (needNeg)
            {
                negative = _negative;
            }

            if (SkillTree.Learn(_good, negative, out string why))
            {
                _good = _negative = _message = null;
            }
            else
            {
                _message = Capital(why);
            }
        }
    }
}
