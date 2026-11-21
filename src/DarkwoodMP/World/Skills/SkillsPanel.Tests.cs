using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.World
{
    // What the tests read and drive on the panel.
    public static partial class SkillsPanel
    {
        // ---- tests ----------------------------------------------------------

        /// <summary>For tests: the key pressed (as Update sees it).</summary>
        internal static void TestPressKey() => Toggle();

        /// <summary>
        /// For tests: choose and learn on the panel, as the clicks do; the
        /// result (null: learned).
        /// </summary>
        internal static string TestLearn(string good, string negative)
        {
            _good = good;
            _negative = negative;
            _message = null;
            LearnChosen();
            return _message;
        }

        /// <summary>
        /// For tests: whether the Learn button would take this choice now (as
        /// DrawFooter enables it).
        /// </summary>
        internal static bool TestReady(string good, string negative)
        {
            var p = Player.Instance;
            if (p == null || SkillTree.Waiting)
            {
                return false;
            }

            var chosen = SkillTree.Chosen(p);
            bool needNeg = SkillTree.NeedsNegative(chosen, good);
            string needed = null;
            if (needNeg)
            {
                needed = negative;
            }

            return SkillTree.Check(p.currentLevel, chosen, good, needed)
                == null;
        }

        /// <summary>
        /// For tests: show this skill's details as if the mouse were over it.
        /// </summary>
        internal static void TestPick(string good, string negative)
        {
            _good = good;
            _negative = negative;
        }

        /// <summary>
        /// For tests: the mouse as the panel sees it (screen pixels from the
        /// top left); null: the real mouse.
        /// </summary>
        internal static Vector2? TestMouse;

        /// <summary>
        /// For tests: the skill the mouse is over (as the last frame drew it).
        /// </summary>
        internal static string TestHover => _hover;

        /// <summary>
        /// For tests: the good skill chosen on the panel (by a click), not
        /// learned yet.
        /// </summary>
        internal static string TestChosen => _good;

        /// <summary>
        /// For tests: a skill's icon and its cell (the hover and click area) as
        /// last drawn, in screen pixels from the top left.
        /// </summary>
        internal static bool TestRects(
            string skill, out Rect icon, out Rect cell)
        {
            icon = cell = default;
            if (!_rects.TryGetValue(skill, out var rs))
            {
                return false;
            }

            icon = rs[0];
            cell = rs[1];
            return true;
        }

        /// <summary>
        /// For tests: the test mouse goes over every skill's cell in turn,
        /// three frames each, and every frame the window's size, whether its
        /// body scrolls, its content's height and the first skill's cell must
        /// stay as they were with the mouse over nothing. Returns the number of
        /// frames where something moved (logged as FAIL lines).
        /// </summary>
        internal static System.Collections.IEnumerator TestHoverSweep(
            Action<int> done)
        {
            TestMouse = new Vector2(-100, -100);
            yield return null;
            yield return null;
            var skills = new List<string>(_rects.Keys);
            string first = null;
            if (skills.Count > 0)
            {
                first = skills[0];
            }

            Rect firstCell = default;
            if (first != null)
            {
                firstCell = _rects[first][1];
            }

            Rect win = Win.Rect;
            bool scrolls = Win.TestScrolls;
            float content = Win.TestContentHeight;
            Plugin.Log.LogInfo(
                $"[skills] hover sweep over {skills.Count} skills: window "
                    + $"{win.width}x{win.height}, scrolls {scrolls}, content "
                    + $"{content}");
            int bad = 0, frames = 0;
            foreach (var sk in skills)
            {
                var cell = _rects[sk][1];
                TestMouse = cell.center;
                for (int i = 0; i < 3; i++)
                {
                    yield return null;
                    frames++;
                    Rect fc = default;
                    if (first != null)
                    {
                        fc = _rects[first][1];
                    }

                    if (Win.Rect.size != win.size
                        || Win.TestScrolls != scrolls
                        || Mathf.Abs(Win.TestContentHeight - content) > 0.5f
                        || fc != firstCell)
                    {
                        bad++;
                        string hover = _hover;
                        if (hover == null)
                        {
                            hover = "nothing";
                        }

                        Plugin.Log.LogInfo(
                            $"[skills] FAIL hovering {sk} ({hover}): window "
                                + $"{Win.Rect.width}x{Win.Rect.height}, "
                                + $"scrolls {Win.TestScrolls}, content "
                                + $"{Win.TestContentHeight}, first cell {fc} "
                                + $"(was {firstCell})");
                    }
                }
            }

            TestMouse = null;
            string text;
            if (bad == 0)
            {
                text = "PASS nothing moved";
            }
            else
            {
                text = $"FAIL {bad} frames moved";
            }

            Plugin.Log.LogInfo(
                $"[skills] hover sweep: {frames} frames, {text}");
            if (done != null)
            {
                done(bad);
            }
        }

        /// <summary>
        /// For tests: spend every point with the real mouse (RealMouse): for
        /// each, a click on a skill the rules allow, one on its negative skill
        /// if one must go with it, and one on Learn, as a player does. Every
        /// click is logged with what the panel made of it. done(failures).
        /// </summary>
        internal static System.Collections.IEnumerator TestClickAll(
            Action<List<string>> done)
        {
            var fails = new List<string>();
            var p = Player.Instance;
            int n = 0;
            if (!Open)
            {
                Toggle();
            }

            yield return new WaitForSecondsRealtime(1f);
            while (p != null && Open && SkillTree.Points(p) > 0 && n < 10)
            {
                n++;
                var choice = SkillTree.AnyChoice(p);
                if (choice == null)
                {
                    fails.Add(
                        $"{SkillTree.Points(p)} points but nothing to learn");
                    break;
                }

                int before = SkillTree.Points(p);
                foreach (var sk in new[] { choice[0], choice[1] })
                {
                    if (sk == null)
                    {
                        continue;
                    }

                    if (!_rects.TryGetValue(sk, out var rs))
                    {
                        fails.Add($"{sk} not drawn");
                        continue;
                    }

                    bool reached = false;
                    yield return RealMouse.MoveTo(
                        rs[1].center, ok => reached = ok);
                    yield return RealMouse.Click();
                    Vector2 mouse = RealMouse.Now;
                    Rect cell = rs[1];
                    string over = _hover;
                    if (over == null)
                    {
                        over = "nothing";
                    }

                    string picked = _good;
                    if (picked == null)
                    {
                        picked = "nothing";
                    }

                    string pickedNegative = _negative;
                    if (pickedNegative == null)
                    {
                        pickedNegative = "nothing";
                    }

                    string why = _message;
                    if (why == null)
                    {
                        why = "none";
                    }

                    Plugin.Log.LogInfo(
                        $"[skills] real click {n} on {sk} (reached {reached}, "
                            + $"mouse {mouse}, cell {cell}): over {over}, "
                            + $"chosen {picked} with {pickedNegative}, message "
                            + $"{why}");
                }

                float until = Time.realtimeSinceStartup + 8f;
                while (SkillTree.Waiting && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }

                yield return null;
                yield return RealMouse.MoveTo(_learnRect.center);
                yield return RealMouse.Click();
                yield return new WaitForSecondsRealtime(0.3f);
                until = Time.realtimeSinceStartup + 8f;
                while (SkillTree.Waiting && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(0.3f);
                var have = SkillTree.Chosen(p);
                bool ok2 = have.Contains(choice[0])
                    && (choice[1] == null || have.Contains(choice[1]));
                Vector2 pointer = RealMouse.Now;
                Rect button = _learnRect;
                string outcome = "NOT learned";
                if (ok2)
                {
                    outcome = "learned";
                }

                string skill = choice[0];
                string withNegative = "";
                if (choice[1] != null)
                {
                    withNegative = " with " + choice[1];
                }

                int after = SkillTree.Points(p);
                bool panelOpen = Open;
                string stillPicked = _good;
                if (stillPicked == null)
                {
                    stillPicked = "nothing";
                }

                string reason = _message;
                if (reason == null)
                {
                    reason = "none";
                }

                Plugin.Log.LogInfo(
                    $"[skills] real click {n} on Learn (mouse {pointer}, "
                        + $"button {button}): {outcome} {skill}{withNegative}, "
                        + $"points {before} -> {after}, panel open "
                        + $"{panelOpen}, chosen {stillPicked}, message "
                        + $"{reason}");
                if (!ok2)
                {
                    string text = "";
                    if (choice[1] != null)
                    {
                        text = "+" + choice[1];
                    }

                    string good = _good;
                    if (good == null)
                    {
                        good = "nothing";
                    }

                    string negative = _negative;
                    if (negative == null)
                    {
                        negative = "nothing";
                    }

                    string message = _message;
                    if (message == null)
                    {
                        message = "none";
                    }

                    fails.Add(
                        $"choice {n} ({choice[0]}{text}) not learned: chosen "
                            + $"{good} with {negative}, message {message}");
                    break;
                }
            }

            if (!Open)
            {
                fails.Add("the panel closed");
            }

            if (p != null && SkillTree.Points(p) > 0 && fails.Count == 0)
            {
                fails.Add($"{SkillTree.Points(p)} points left");
            }

            if (SkillTree.NegativesWrong(p) is string wrong)
            {
                fails.Add("negative skills: " + wrong);
            }

            if (done != null)
            {
                done(fails);
            }
        }

        /// <summary>
        /// For tests: the panel's window, in screen pixels from the top left.
        /// </summary>
        internal static Rect TestWindow => Win.Rect;
    }
}
