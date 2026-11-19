using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for skill points and the morning skill reset (World.SkillTree, SkillsPanel, Progression).
    /// The list is tools/tests/map_scenario_skills.py.
    /// </summary>
    public static class MapScenarioSkills
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            Add("skills-give", Give);
            Add("skills-panel", Panel);
            Add("skills-learn", Learn);
            Add("skills-learn-all", LearnAll, timeout: 60f);
            Add("skills-reset", Reset, timeout: 40f);
        }

        /// <summary>"skills-give N": N more levels for our player (their points to spend, no skills chosen); a negative N takes levels away.</summary>
        private static IEnumerator Give(string[] a, Result r)
        {
            int n = a.Length > 0 ? int.Parse(a[0]) : 1;
            Progression.TestGiveLevels(n, chooseSkills: false);
            yield return new WaitForSecondsRealtime(1.5f); // the host takes in its own level (Progression.Update)
            r.Info = Progression.TestState();
        }

        /// <summary>
        /// "skills-panel": the key opens the Skills panel without the backpack; every skill's hover and click area
        /// (its cell) covers its drawn icon, the mouse at an icon's edge and between two icons is over a skill, and
        /// the window keeps its size whether a skill is shown or not; the backpack opened with its own key leaves
        /// the panel open; the key again closes the panel.
        /// </summary>
        private static IEnumerator Panel(string[] a, Result r)
        {
            var fails = new List<string>();
            if (P.Inventory.open)
            {
                P.getIntoInventory();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            SkillsPanel.TestPressKey();
            yield return new WaitForSecondsRealtime(2f);
            bool open = SkillsPanel.Open, bag = P.Inventory.open || P.gettingInventory;
            if (!open)
            {
                fails.Add("the panel didn't open");
            }
            if (bag)
            {
                fails.Add("the key opened the backpack");
            }

            // The hit areas, as drawn on the last frame.
            int skills = 0;
            string first = null, below = null;
            Rect firstIcon = default, firstCell = default;
            foreach (var t in SkillTree.Tiers)
            {
                foreach (var list in new[] { t.skills, t.negativeSkills })
                {
                    for (int k = 0; k < list.Count; k++)
                    {
                        var sk = list[k];
                        if (sk == null)
                        {
                            continue;
                        }
                        if (!SkillsPanel.TestRects(sk.name, out var icon, out var cell))
                        {
                            fails.Add($"{sk.name} not drawn");
                            continue;
                        }
                        skills++;
                        if (!(cell.xMin <= icon.xMin && cell.yMin <= icon.yMin && cell.xMax >= icon.xMax && cell.yMax >= icon.yMax))
                        {
                            fails.Add($"{sk.name}: cell {cell} doesn't cover icon {icon}");
                        }
                        if (first == null && k + 1 < list.Count && list[k + 1] != null)
                        {
                            first = sk.name;
                            below = list[k + 1].name;
                            firstIcon = icon;
                            firstCell = cell;
                        }
                    }
                }
            }
            float hBefore = SkillsPanel.TestWindow.height;
            string atEdge = null, atGap = null, outside = null;
            if (first != null)
            {
                // The icon's bottom right pixel, then the middle of the space between it and the icon below.
                SkillsPanel.TestMouse = new Vector2(firstIcon.xMax - 1f, firstIcon.yMax - 1f);
                yield return null;
                yield return null;
                atEdge = SkillsPanel.TestHover;
                SkillsPanel.TestRects(below, out var belowIcon, out _);
                SkillsPanel.TestMouse = new Vector2(firstIcon.center.x, (firstIcon.yMax + belowIcon.yMin) / 2f);
                yield return null;
                yield return null;
                atGap = SkillsPanel.TestHover;
                float hHover = SkillsPanel.TestWindow.height;
                SkillsPanel.TestMouse = new Vector2(1f, 1f);
                yield return null;
                yield return null;
                outside = SkillsPanel.TestHover;
                SkillsPanel.TestMouse = null;
                if (atEdge != first)
                {
                    fails.Add($"mouse at {first}'s icon edge: over {atEdge ?? "nothing"}");
                }
                if (atGap != first && atGap != below)
                {
                    fails.Add($"mouse between {first} and {below}: over {atGap ?? "nothing"}");
                }
                if (outside != null)
                {
                    fails.Add($"mouse outside the panel: over {outside}");
                }
                if (Mathf.Abs(hHover - hBefore) > 0.5f || Mathf.Abs(SkillsPanel.TestWindow.height - hBefore) > 0.5f)
                {
                    fails.Add($"the window's height changed with the hover: {hBefore} -> {hHover} -> {SkillsPanel.TestWindow.height}");
                }
            }
            else
            {
                fails.Add("no two skills in a column to test the hover with");
            }

            // The backpack with its own key: both stay open.
            P.getIntoInventory();
            yield return new WaitForSecondsRealtime(2f);
            bool both = SkillsPanel.Open && P.Inventory.open;
            if (!both)
            {
                fails.Add($"backpack opened: panel {SkillsPanel.Open}, backpack {P.Inventory.open}");
            }
            if (P.Inventory.open)
            {
                P.getIntoInventory();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            bool stillOpen = SkillsPanel.Open;
            if (!stillOpen)
            {
                fails.Add("closing the backpack closed the panel");
            }
            if (SkillsPanel.Open)
            {
                SkillsPanel.TestPressKey();
            }
            yield return new WaitForSecondsRealtime(1f);
            r.Info = $"open {open} (backpack {bag}); {skills} skills, cell {firstCell} over icon {firstIcon}; mouse at the edge: {atEdge}, " +
                $"between: {atGap}, outside: {outside ?? "nothing"}; height {hBefore}; with the backpack both open {both}, " +
                $"after it closed panel open {stillOpen}; closed {!SkillsPanel.Open}";
            if (SkillsPanel.Open)
            {
                fails.Add("the key didn't close the panel");
            }
            if (fails.Count > 0)
            {
                r.Fail(string.Join("; ", fails) + " | " + r.Info);
            }
        }

        /// <summary>"skills-learn [--refused]": learn a skill the rules allow (with its negative one if needed) and wait
        /// for the host's answer; --refused: the host refuses it and it's taken back.</summary>
        private static IEnumerator Learn(string[] a, Result r)
        {
            bool refused = a.Contains("--refused");
            var choice = SkillTree.AnyChoice(P);
            if (choice == null)
            {
                r.Fail($"nothing to learn: {Progression.TestState()}");
                yield break;
            }
            int before = SkillTree.Points(P);
            string why = SkillsPanel.TestLearn(choice[0], choice[1]);
            if (why != null)
            {
                r.Fail($"{choice[0]}: {why}");
                yield break;
            }
            float until = Time.realtimeSinceStartup + 8f;
            while (SkillTree.Waiting && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.3f);
            bool have = SkillTree.Chosen(P).Contains(choice[0]);
            r.Info = $"{choice[0]}{(choice[1] != null ? " with " + choice[1] : "")}: have it {have}, points {before} -> {SkillTree.Points(P)}, waiting {SkillTree.Waiting}";
            if (SkillTree.Waiting || have == refused)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "skills-learn-all": with the panel open, spend every point one after another, as a player clicks a skill
        /// and Learn each time (the next as soon as the Learn button takes it). Every choice must be taken, the panel
        /// must stay open, and every skill learned must still be there at the end, with no points left.
        /// </summary>
        private static IEnumerator LearnAll(string[] a, Result r)
        {
            var fails = new List<string>();
            if (!SkillsPanel.Open)
            {
                SkillsPanel.TestPressKey();
            }
            yield return new WaitForSecondsRealtime(1f);
            if (!SkillsPanel.Open)
            {
                r.Fail("the panel didn't open");
                yield break;
            }
            int start = SkillTree.Points(P), n = 0;
            var learned = new List<string>();
            var log = new List<string>();
            while (SkillTree.Points(P) > 0 && n < 12)
            {
                var choice = SkillTree.AnyChoice(P);
                if (choice == null)
                {
                    fails.Add($"{SkillTree.Points(P)} points but nothing to learn");
                    break;
                }
                // The Learn button takes a click once it's ready (not while a choice waits for the host).
                float until = Time.realtimeSinceStartup + 8f;
                while (!SkillsPanel.TestReady(choice[0], choice[1]) && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
                int before = SkillTree.Points(P);
                string why = SkillsPanel.TestLearn(choice[0], choice[1]);
                n++;
                log.Add($"{choice[0]}{(choice[1] != null ? "+" + choice[1] : "")}: {why ?? "ok"} ({before} -> {SkillTree.Points(P)})");
                if (why != null)
                {
                    fails.Add($"choice {n} ({choice[0]}): {why}");
                    break;
                }
                learned.Add(choice[0]);
                if (choice[1] != null)
                {
                    learned.Add(choice[1]);
                }
                yield return null;
                if (!SkillsPanel.Open)
                {
                    fails.Add($"the panel closed after choice {n}");
                    break;
                }
            }
            float end = Time.realtimeSinceStartup + 8f;
            while (SkillTree.Waiting && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            var have = SkillTree.Chosen(P);
            var lost = learned.Where(s => !have.Contains(s)).ToList();
            if (lost.Count > 0)
            {
                fails.Add($"taken back: {string.Join(", ", lost)}");
            }
            if (SkillTree.Points(P) != 0)
            {
                fails.Add($"{SkillTree.Points(P)} points left");
            }
            if (SkillTree.Waiting)
            {
                fails.Add("still waiting for the host");
            }
            if (!SkillsPanel.Open)
            {
                fails.Add("the panel isn't open");
            }
            // As the levels one at a time give them: one negative skill for each tier, with its first skill.
            if (SkillTree.NegativesWrong(P) is string wrong)
            {
                fails.Add("negative skills: " + wrong);
            }
            if (SkillsPanel.Open)
            {
                SkillsPanel.TestPressKey();
            }
            r.Info = $"{start} points, {n} choices: {string.Join("; ", log)}; {Progression.TestState()}";
            if (start < 2)
            {
                fails.Add("fewer than two points to spend");
            }
            if (fails.Count > 0)
            {
                r.Fail(string.Join("; ", fails) + " | " + r.Info);
            }
        }

        /// <summary>"skills-reset on|off": reset at the stove; on: allowed (every skill back, the points again), off: refused.</summary>
        private static IEnumerator Reset(string[] a, Result r)
        {
            bool want = a.Length == 0 || a[0] == "on";
            var menu = Singleton<UI>.Instance.LevelingMenu;
            menu.show();
            float until = Time.realtimeSinceStartup + 20f;
            while (!Progression.TestCanReset && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (!Progression.TestCanReset)
            {
                r.Fail("the stove's menu didn't open");
                yield break;
            }
            int had = SkillTree.Chosen(P).Count;
            Progression.TestReset();
            until = Time.realtimeSinceStartup + 8f;
            while (Progression.TestWaitingReset && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            int left = SkillTree.Chosen(P).Count;
            r.Info = $"skills {had} -> {left}, morning {Progression.InMorning(0)}: {Progression.TestState()}";
            menu.hide();
            yield return new WaitForSecondsRealtime(2f);
            if (had == 0 || (want ? left != 0 : left != had))
            {
                r.Fail(r.Info);
            }
        }
    }
}
