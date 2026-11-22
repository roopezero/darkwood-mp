using System;
using System.Collections.Generic;
using DarkwoodMP.Menus;
using DarkwoodMP.World.Skills;
using UnityEngine;

namespace DarkwoodMP.World
{
    // The panel's body and footer.
    internal sealed partial class SkillsWindow
    {
        // ---- drawing --------------------------------------------------------

        private float Icon => 40 * UiWindow.S;

        private void Draw(Flow f)
        {
            var p = Player.Instance;
            var tiers = SkillTree.Tiers;
            if (p == null || tiers == null)
            {
                Ui.Text(f, "No skills here.");
                return;
            }

            var choice = SkillTree.ChoiceOf(p);
            var chosen = choice.Chosen;
            int level = choice.Level,
                points = SkillRules.Points(choice.Tiers, level, chosen);
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
                    bool can = SkillPanelRules.GoodAllowed(
                        choice with { Good = sk.name });
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
                        bool can = SkillPanelRules.NegativeAllowed(
                            choice with { Good = _good, Negative = sk.name });
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
                bool neg = SkillRules.Find(choice.Tiers, show).Negative;
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
                var detail = SkillPanelRules.Detail(choice, show);
                string state = SkillPanelTexts.Text(detail.Note);
                if (detail.Note == PanelNote.Refused)
                {
                    state = Refusals.Text(detail.Verdict);
                }

                Ui.Label(f, SkillPanelTexts.Capital(state), MenuSkin.Dim);
                Ui.Text(f, SkillDesc(show));
            }
            else
            {
                Ui.Text(f, SkillPanelTexts.Hint(points), MenuSkin.Dim);
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
        private void MeasureDetails(
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

        private void DrawFooter(Rect r)
        {
            var p = Player.Instance;
            if (p == null)
            {
                return;
            }

            var choice = SkillTree.ChoiceOf(p) with
            {
                Good = _good,
                Negative = _negative,
            };

            bool waiting = SkillTree.Waiting;
            var c = Ui.Split(r, 0, 90);
            var note = SkillPanelRules.Footer(
                new FooterFacts
                {
                    HasMessage = _message != null,
                    Waiting = waiting,
                    Good = _good,
                    Negative = _negative,
                    Points = SkillRules.Points(
                        choice.Tiers, choice.Level, choice.Chosen),
                    NeedsNegative = _good != null
                        && SkillRules.NeedsNegative(
                            choice.Tiers, choice.Chosen, _good),
                });

            string status = SkillPanelTexts.Text(note);
            if (note == PanelNote.Message)
            {
                status = _message;
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
            bool ready = SkillPanelRules.Ready(choice, waiting);

            if (Event.current.type == EventType.Repaint)
            {
                _learnRect = ToScreen(c[1]);
            }

            if (Ui.Button(c[1], "Learn", ready))
            {
                LearnChosen();
            }
        }

        private void LearnChosen()
        {
            var p = Player.Instance;
            string negative = null;
            if (p != null)
            {
                negative = SkillPanelRules.NegativeSent(
                    SkillTree.ChoiceOf(p) with
                    {
                        Good = _good,
                        Negative = _negative,
                    });
            }

            if (SkillTree.Learn(_good, negative, out string why))
            {
                _good = _negative = _message = null;
            }
            else
            {
                _message = SkillPanelTexts.Capital(why);
            }
        }
    }
}
