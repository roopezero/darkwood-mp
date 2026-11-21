using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Skills;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Spending skill points, from the Skills panel (SkillsPanel), at any time
    /// and anywhere.
    ///
    /// In the game a full essence bar at the stove raises Player.currentLevel
    /// and opens the skill choice at once (SkillsMenu.showHideTiers,
    /// confirmSkills). Here the stove only gives the level
    /// (Progression.TakeLevelAtStove), and each level is a point to spend: a
    /// player's unspent points are their level less the good skills they have.
    /// The game keeps both in its save, so the points need no file of their
    /// own.
    ///
    /// The game's rules for a choice still hold, as SkillsMenu and SkillTiers
    /// apply them: a tier (I to IV) can be chosen from once the player's level
    /// reaches the tier's level (SkillTiers.getTiers); a tier takes two good
    /// skills at most (Tier.locked); and each tier with negative skills gives
    /// one of them with its first good skill. The game says the last one as
    /// "while an open tier has no good skill yet, a choice comes with a
    /// negative skill from such a tier" (SkillsMenu.mustChooseNegativeSkill),
    /// which with one choice per level comes to the same:
    /// a tier opens at a level, and the tiers below are full by then. With
    /// several points banked that wording asked for a negative skill with every
    /// choice while any open tier had no good skill (three or four negative
    /// skills at level 3 instead of two), so here the negative skill goes with
    /// the first good skill of its own tier.
    ///
    /// In co-op a client learns at once and asks the host (SkillSpend, a
    /// request) with its level and the skills it has. The host checks the
    /// choice against the same rules and against the shared level (the list of
    /// skills is the client's word: they are its own character's); a refused
    /// choice is taken back. The skills are each player's own (their character,
    /// World.Characters), so only the client's own game changes.
    /// </summary>
    public static class SkillTree
    {
        private static Session S => Session.Instance;

        public static void Register(Session s)
        {
            s.Register(MsgType.SkillSpend, OnSpend);
            s.OnSessionEnded += () => _waiting = false;
        }

        // ---- the game's tiers -----------------------------------------------

        private static SkillTiers _tiers;

        /// <summary>
        /// The game's skill tiers (Resources/SkillTiers, as SkillsMenu loads
        /// it), or null before the world is up.
        /// </summary>
        public static List<SkillTiers.Tier> Tiers
        {
            get
            {
                if (_tiers == null)
                {
                    var menu = Singleton<SkillsMenu>.Instance;
                    if (menu != null)
                    {
                        _tiers = AccessTools.Field(
                            typeof(SkillsMenu),
                            "skillTiers").GetValue(menu) as SkillTiers;
                    }

                    if (_tiers == null)
                    {
                        var go = Resources.Load("SkillTiers") as GameObject;
                        if (go != null)
                        {
                            _tiers = go.GetComponent<SkillTiers>();
                        }
                    }
                }

                if (_tiers != null)
                {
                    return _tiers.tiers;
                }

                return null;
            }
        }

        /// <summary>
        /// The facts of the game's tiers for the rules (SkillRules), without
        /// the null entries; null for no tiers.
        /// </summary>
        public static TierFacts[] FactsOf(List<SkillTiers.Tier> tiers)
        {
            if (tiers == null)
            {
                return null;
            }

            var facts = new TierFacts[tiers.Count];
            for (int i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                facts[i] = new TierFacts
                {
                    Level = t.level,
                    Good = Names(t.skills),
                    Negative = Names(t.negativeSkills),
                };
            }

            return facts;
        }

        private static string[] Names(List<PlayerSkill> skills)
        {
            var names = new List<string>();
            foreach (var sk in skills)
            {
                if (sk != null)
                {
                    names.Add(sk.name);
                }
            }

            return names.ToArray();
        }

        /// <summary>The facts of the game's tiers (Tiers).</summary>
        public static TierFacts[] Facts => FactsOf(Tiers);

        /// <summary>
        /// Our player's level and skills with the tiers, for the rules; no
        /// skill picked yet (Good and Negative null).
        /// </summary>
        public static ChoiceFacts ChoiceOf(Player p)
        {
            return new ChoiceFacts
            {
                Tiers = Facts,
                Level = p.currentLevel,
                Chosen = Chosen(p),
            };
        }

        /// <summary>
        /// A skill (good or negative) by name, as SkillRules.Find finds it;
        /// null: none.
        /// </summary>
        private static PlayerSkill Find(string name)
        {
            var tiers = Tiers;
            var spot = SkillRules.Find(FactsOf(tiers), name);
            if (!spot.Found)
            {
                return null;
            }

            var tier = tiers[spot.Tier];
            var skills = tier.skills;
            if (spot.Negative)
            {
                skills = tier.negativeSkills;
            }

            foreach (var sk in skills)
            {
                if (sk != null && sk.name == name)
                {
                    return sk;
                }
            }

            return null;
        }

        /// <summary>The tier's name as the game shows it: I to IV.</summary>
        public static string TierName(int index) =>
            SkillRules.TierName(index);

        // ---- the player's skills --------------------------------------------

        /// <summary>The names of the tier skills our player has.</summary>
        public static HashSet<string> Chosen(Player p)
        {
            var set = new HashSet<string>();
            var tiers = Tiers;
            if (tiers == null || p == null)
            {
                return set;
            }

            foreach (var t in tiers)
            {
                foreach (var sk in t.skills)
                {
                    if (sk != null && sk.chosen)
                    {
                        set.Add(sk.name);
                    }
                }

                foreach (var sk in t.negativeSkills)
                {
                    if (sk != null && sk.chosen)
                    {
                        set.Add(sk.name);
                    }
                }
            }

            return set;
        }

        /// <summary>
        /// Points to spend: one for each level taken, less the good skills had
        /// (SkillRules.Points).
        /// </summary>
        public static int Points(Player p)
        {
            if (p == null)
            {
                return 0;
            }

            return SkillRules.Points(Facts, p.currentLevel, Chosen(p));
        }


        // ---- learning -------------------------------------------------------

        private static bool _waiting;
        /// <summary>
        /// Client: a choice is waiting for the host's answer.
        /// </summary>
        public static bool Waiting => _waiting;

        /// <summary>
        /// Take a good skill (and the negative one that must go with it, or
        /// null), as SkillsMenu.confirmSkills gives them.
        /// </summary>
        public static bool Learn(string good, string negative, out string why)
        {
            var p = Player.Instance;
            why = null;
            var note = SkillRules.MayLearn(
                p != null && p.skills != null && Tiers != null, _waiting);
            if (note != SkillNote.None)
            {
                why = SkillNotes.Text(note);
                return false;
            }

            var choice = ChoiceOf(p) with { Good = good, Negative = negative };
            var chosen = choice.Chosen;
            int level = choice.Level;
            var verdict = SkillRules.Check(choice);
            if (verdict.Refused)
            {
                why = Refusals.Text(verdict);
                return false;
            }

            var gs = Find(good);
            var ns = Find(negative);
            Give(gs, ns);
            string text = "";
            if (ns != null)
            {
                text = " with " + negative;
            }

            Plugin.Log.LogInfo(
                $"[skills] learned {good}{text}: {Points(p)} points left");
            if (NetEnemies.ClientMode && WorldShare.InWorld && S != null
                && S.IsClient)
            {
                _waiting = true;
                var w = S.Begin(MsgType.SkillSpend);
                SkillSpend.Write(
                    w,
                    new SpendRequest
                    {
                        Level = level,
                        Good = good,
                        Negative = negative,
                        Had = chosen.ToList(),
                    });

                Coop.Current.Authority.Request(
                    w, $"learn {good}", (ok, reason, _) =>
                {
                    _waiting = false;
                    if (ok)
                    {
                        return;
                    }

                    Take(gs);
                    Take(ns);
                    Plugin.Log.LogInfo(
                        $"[skills] the host refused {good}: {reason}; taken "
                            + "back");
                    Player instance = Player.Instance;
                    if (instance != null)
                    {
                        instance.displayMessage(reason);
                    }
                }, inventory: false);
            }

            return true;
        }

        private static void Give(PlayerSkill good, PlayerSkill negative)
        {
            var p = Player.Instance;
            if (negative != null)
            {
                negative.initialize(destBool: true);
            }

            good.initialize(destBool: true);
            if (good.isActivatedManually)
            {
                Flags instance = Singleton<Flags>.Instance;
                if (instance != null)
                {
                    instance.setFlag(
                        "player_firstActiveSkillObtained",
                        activeModifier: true);
                }
            }

            p.skills.refreshAll();
            p.gainExp(0);
            try
            {
                AudioController.Play("UI_placeItem");
            }
            catch
            {
            }

            SkillsMenu menu = Singleton<SkillsMenu>.Instance;
            if (menu != null)
            {
                menu.tryShowActiveSkillsHelpPopup();
            }
        }

        private static void Take(PlayerSkill sk)
        {
            if (sk == null || !sk.chosen)
            {
                return;
            }

            sk.initialize(destBool: false);
            Player instance = Player.Instance;
            if (instance != null)
            {
                PlayerSkills skills = instance.skills;
                if (skills != null)
                {
                    skills.refreshAll();
                }
            }
        }

        // ---- host -----------------------------------------------------------

        private static void OnSpend(ulong from, PacketReader r)
        {
            var spend = SkillSpend.Read(r);
            int level = spend.Level;
            string good = spend.Good, negative = spend.Negative;
            var chosen = new HashSet<string>(spend.Had);

            var authority = Coop.Current.Authority;
            if (!SkillRules.Handles(S.IsHost, authority.Handling))
            {
                return;
            }

            var tiers = Facts;
            int most = 0;
            if (tiers != null)
            {
                most = Progression.MostLevels(Player.Instance);
            }

            var verdict = SkillRules.MaySpend(
                new SpendFacts
                {
                    Choice = new ChoiceFacts
                    {
                        Tiers = tiers,
                        Level = level,
                        Chosen = chosen,
                        Good = good,
                        Negative = negative,
                    },
                    Most = most,
                });

            if (verdict.Refused)
            {
                authority.Refuse(verdict);
                return;
            }

            string name = S.NameOf(from);
            string withNegative = "";
            if (negative.Length > 0)
            {
                withNegative = " with " + negative;
            }

            Plugin.Log.LogInfo(
                $"[skills] {name} learned {good}{withNegative} at level "
                    + $"{level}");
        }

        // ---- tests ----------------------------------------------------------

        /// <summary>For tests: every tier with its level and skills.</summary>
        internal static string Describe()
        {
            var tiers = Tiers;
            if (tiers == null)
            {
                return "no tiers";
            }

            var parts = new List<string>();
            for (int i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                string skills = string.Join(
                    ", ", t.skills.Where(s => s != null).Select(s => s.name));
                string negatives = string.Join(
                    ", ",
                    t.negativeSkills.Where(s => s != null).Select(s => s.name));
                parts.Add(
                    $"tier {TierName(i)} (level {t.level}): "
                        + $"{skills}; " +
                          "negative: "
                        + $"{negatives}");
            }

            return string.Join(" | ", parts);
        }

        /// <summary>
        /// For tests: what breaks "one negative skill for each tier that has a
        /// good skill" in our player's skills; null: nothing.
        /// </summary>
        internal static string NegativesWrong(Player p)
        {
            var tiers = Tiers;
            if (tiers == null || p == null)
            {
                return null;
            }

            var chosen = Chosen(p);
            var wrong = new List<string>();
            for (int i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                int negs = t.negativeSkills.Count(
                    sk => sk != null && chosen.Contains(sk.name));
                int goods = t.skills.Count(
                    sk => sk != null && chosen.Contains(sk.name));
                bool good = goods > 0,
                    owes = t.negativeSkills.Any(sk => sk != null);
                if (negs > 1 || (good && owes && negs != 1)
                    || (!good && negs > 0))
                {
                    wrong.Add(
                        $"tier {TierName(i)}: {negs} negative skills with "
                            + $"{goods} skills");
                }
            }

            if (wrong.Count > 0)
            {
                return string.Join(", ", wrong);
            }

            return null;
        }

        /// <summary>
        /// For tests: a choice that the rules allow now (good, negative or
        /// null), or null if there's none.
        /// </summary>
        internal static string[] AnyChoice(Player p)
        {
            var tiers = Tiers;
            if (tiers == null || p == null)
            {
                return null;
            }

            var choice = ChoiceOf(p);
            foreach (var t in tiers)
            {
                foreach (var g in t.skills)
                {
                    if (g == null
                        || SkillRules.MayTake(choice with { Good = g.name })
                            .Refused)
                    {
                        continue;
                    }

                    if (!SkillRules.NeedsNegative(
                        choice.Tiers, choice.Chosen, g.name))
                    {
                        return new[] { g.name, null };
                    }

                    foreach (var ng in t.negativeSkills)
                    {
                        if (ng != null
                            && !SkillRules.Check(
                                choice with
                                {
                                    Good = g.name,
                                    Negative = ng.name,
                                }).Refused)
                        {
                            return new[] { g.name, ng.name };
                        }
                    }
                }
            }

            return null;
        }
    }
}
