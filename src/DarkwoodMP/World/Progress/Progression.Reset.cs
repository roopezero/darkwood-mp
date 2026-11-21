using System;
using System.Collections.Generic;
using System.IO;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Progress
{
    // The skill reset at the stove, the stove's overlay, and the tests' hooks.
    internal sealed partial class Progression
    {
        /// <summary>
        /// Take back all of a player's chosen skills (good and negative).
        /// keepLevel: the levels stay, and the points come back to spend again
        /// (a reset); otherwise Player.currentLevel goes to 0 (a new character,
        /// which takes the shared levels at the stove).
        /// </summary>
        public static void ClearSkills(Player p, bool keepLevel = false)
        {
            var ps = p.skills;
            if (ps == null)
            {
                return;
            }

            var all = new HashSet<PlayerSkill>();
            foreach (var sk in ps.skills)
            {
                if (sk != null)
                {
                    all.Add(sk);
                }
            }

            foreach (var sk in ps.progressionSkills)
            {
                if (sk != null)
                {
                    all.Add(sk);
                }
            }

            var menu = Singleton<SkillsMenu>.Instance;
            var tiers = SkillTree.Tiers;
            if (tiers != null)
            {
                foreach (var t in tiers)
                {
                    foreach (var sk in t.skills)
                    {
                        if (sk != null)
                        {
                            all.Add(sk);
                        }
                    }

                    foreach (var sk in t.negativeSkills)
                    {
                        if (sk != null)
                        {
                            all.Add(sk);
                        }
                    }
                }
            }

            int n = 0;
            foreach (var sk in all)
            {
                if (sk.chosen)
                {
                    sk.initialize(destBool: false);
                    n++;
                }
            }

            ps.skills.RemoveAll(sk => sk == null || all.Contains(sk));
            if (menu != null && menu.chosenSkills != null)
            {
                menu.chosenSkills.Clear();
            }

            ps.SkillPoints = 0;
            ps.refreshAll();
            if (!keepLevel)
            {
                p.currentLevel = 0;
            }

            Plugin.Log.LogInfo(
                $"[progress] skills reset: {n} taken back, level "
                    + $"{p.currentLevel}");
        }

        private bool CanResetHere(Player p)
        {
            return SkillResetRules.CanResetHere(StoveOf(p));
        }

        private bool _waitingReset;

        private void ResetHere()
        {
            var p = Player.Instance;
            if (!CanResetHere(p))
            {
                return;
            }

            if (NetEnemies.ClientMode)
            {
                if (!SkillResetRules.Asks(_waitingReset))
                {
                    return;
                }

                _waitingReset = true;
                Coop.Current.Authority.Request(
                    S.Begin(MsgType.SkillReset), "reset skills",
                    (ok, reason, _) =>
                {
                    _waitingReset = false;
                    if (ok)
                    {
                        DoReset(Player.Instance);
                    }
                    else
                    {
                        Player instance = Player.Instance;
                        if (instance != null)
                        {
                            instance.displayMessage(reason);
                        }

                        Plugin.Log.LogInfo(
                            $"[progress] reset refused: {reason}");
                    }
                }, inventory: false);

                return;
            }

            var verdict = TakeReset(SkillResetRules.HostWho, 0);
            if (verdict.Refused)
            {
                string why = Refusals.Text(verdict);
                p.displayMessage(why);
                Plugin.Log.LogInfo($"[progress] reset refused: {why}");
                return;
            }

            DoReset(p);
        }

        private void DoReset(Player p)
        {
            if (p == null)
            {
                return;
            }

            ClearSkills(p, keepLevel: true);
            if (ProgressSideRules.AfterReset(Side()) == ProgressRoute.Client)
            {
                SendLevel(p);
                Apply(p);
            }
            else
            {
                HostSync();
            }

            p.displayMessage(
                $"Skills reset: {SkillTree.Points(p)} skill points to spend "
                    + $"({SkillsPanel.KeyName})");
        }

        /// <summary>At our stove: the levels waiting, the points to spend, and
        /// the reset button in the morning, in the mod's menu style, with a
        /// window box behind them so they read over the world.</summary>
        public void OnGUI()
        {
            var p = Player.Instance;
            if (!StoveRules.OverlayOpen(
                WorldShare.InWorld, UiState.MenuOver, CanResetHere(p)))
            {
                return;
            }

            int waiting = StoveRules.LevelsToTake(
                Hideouts.Together || Solo, SharedLevel, p.currentLevel);
            int points = SkillTree.Points(p);
            bool morning = InMorning(0);
            var lines = new List<string>(2);
            if (waiting > 0)
            {
                lines.Add($"Levels to take: {waiting}");
            }

            if (points > 0)
            {
                lines.Add(
                    $"Skill points to spend: {points} ({SkillsPanel.KeyName}: "
                        + "Skills)");
            }

            if (!StoveRules.OverlayShows(waiting, points, morning))
            {
                return;
            }

            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            float s = Ui.S, pad = 6 * s, line = PixelFont.Line + 2 * s;
            string button = "Reset skills (mornings, once a day)";
            if (_waitingReset)
            {
                button = "Asking the host...";
            }

            float w =
                PixelFont.Width("Reset skills (mornings, once a day)") + 24 * s;
            foreach (var l in lines)
            {
                w = Math.Max(w, PixelFont.Width(l) + 2 * pad);
            }

            float row = 0f;
            if (morning)
            {
                row = Ui.RowH;
                if (lines.Count > 0)
                {
                    row += pad;
                }
            }

            float h = 2 * pad + lines.Count * line + row;
            float x = Mathf.Round((Screen.width - w) / 2f),
                y = Mathf.Round(Screen.height - h - 40 * s);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, MenuSkin.Window);
            float ly = y + pad;
            foreach (var l in lines)
            {
                Ui.Label(
                    new Rect(x + pad, ly, w - 2 * pad, line), l,
                    MenuSkin.Bright, TextAnchor.MiddleCenter);
                ly += line;
            }

            if (morning)
            {
                if (lines.Count > 0)
                {
                    ly += pad;
                }

                if (Ui.Button(
                    new Rect(x + pad, ly, w - 2 * pad, Ui.RowH), button,
                    !_waitingReset))
                {
                    ResetHere();
                }
            }

            GUI.skin = skin;
        }

        /// <summary>
        /// For tests: reset our skills at the stove as the button does.
        /// </summary>
        internal void TestReset() => ResetHere();
        internal bool TestCanReset => CanResetHere(Player.Instance);
        internal bool TestWaitingReset => _waitingReset;

        /// <summary>
        /// For tests: give our player this many levels and the essence they
        /// need (and a skill chosen for each).
        /// </summary>
        internal void TestGiveLevels(int n, bool chooseSkills = true)
        {
            var p = Player.Instance;
            var tiers = SkillTree.Tiers;
            if (p == null || tiers == null)
            {
                return;
            }

            int given = 0;
            if (chooseSkills)
            {
                foreach (var t in tiers)
                {
                    foreach (var sk in t.skills)
                    {
                        if (given < n && sk != null && !sk.chosen)
                        {
                            sk.initialize(destBool: true);
                            given++;
                        }
                    }
                }
            }

            p.skills.refreshAll();
            p.currentLevel += n;
            p.experience = Req(p, p.currentLevel - 1);
        }

        /// <summary>
        /// For tests: our level, essence, the shared numbers and how many
        /// skills are chosen.
        /// </summary>
        internal string TestState()
        {
            var p = Player.Instance;
            if (p == null)
            {
                return "no player";
            }

            int chosen = 0;
            if (p.skills != null)
            {
                foreach (var sk in p.skills.skills)
                {
                    if (sk != null && sk.chosen)
                    {
                        chosen++;
                    }
                }
            }

            return $"level {p.currentLevel}, essence {p.experience} (next "
                + $"level at {Req(p, p.currentLevel)}), shared essence "
                + $"{SharedExp} level {SharedLevel}, " +
                   $"skills chosen {chosen}, points {SkillTree.Points(p)}, "
                + $"morning {InMorning(0)}, kept in a file {_saved != null}, " +
                   "reset today "
                + (_saved != null
                    && _saved.Resets.TryGetValue("host", out int d)
                    && d == Today);
        }
    }
}
