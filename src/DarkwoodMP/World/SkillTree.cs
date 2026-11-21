using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
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
        /// A skill (good or negative) by name, with its tier; null: none.
        /// </summary>
        private static PlayerSkill Find(
            string name, out SkillTiers.Tier tier, out bool negative)
        {
            tier = null;
            negative = false;
            var tiers = Tiers;
            if (tiers == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            foreach (var t in tiers)
            {
                tier = t;
                foreach (var sk in t.skills)
                {
                    if (sk != null && sk.name == name)
                    {
                        return sk;
                    }
                }
                negative = true;
                foreach (var sk in t.negativeSkills)
                {
                    if (sk != null && sk.name == name)
                    {
                        return sk;
                    }
                }
                negative = false;
            }
            tier = null;
            return null;
        }

        private static PlayerSkill Find(string name) =>
            Find(name, out _, out _);

        /// <summary>
        /// The tier a skill (good or negative) is in, by name; null: none.
        /// </summary>
        public static SkillTiers.Tier TierOf(string name, out bool negative)
        {
            Find(name, out var tier, out negative);
            return tier;
        }

        private static readonly string[] TierNames = { "I", "II", "III", "IV" };
        public static string TierName(int index)
        {
            if (index >= 0 && index < TierNames.Length)
            {
                return TierNames[index];
            }
            return (index + 1).ToString();
        }

        // ---- the rules ------------------------------------------------------

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

        private static int Good(IEnumerable<string> chosen)
        {
            int n = 0;
            foreach (var c in chosen)
            {
                if (TierOf(c, out bool neg) != null && !neg)
                {
                    n++;
                }
            }
            return n;
        }

        private static bool HasGood(
            SkillTiers.Tier t, HashSet<string> chosen) =>
            t.skills.Any(sk => sk != null
                    && chosen.Contains(sk.name));
        private static int GoodIn(SkillTiers.Tier t, HashSet<string> chosen) =>
            t.skills.Count(sk => sk != null && chosen.Contains(sk.name));

        /// <summary>
        /// Points to spend: one for each level taken, less the good skills had.
        /// </summary>
        public static int Points(int level, HashSet<string> chosen) =>
            Math.Max(0, level - Good(chosen));
        public static int Points(Player p)
        {
            if (p == null)
            {
                return 0;
            }
            return Points(p.currentLevel, Chosen(p));
        }

        /// <summary>
        /// Whether this good skill must come with a negative skill: it would be
        /// the first good skill of a tier that has negative skills, and none of
        /// them is taken yet (one negative skill for each such tier, as the
        /// game gives it when the levels come one at a time:
        /// SkillsMenu.showHideTiers, mustChooseNegativeSkill).
        /// </summary>
        public static bool NeedsNegative(HashSet<string> chosen, string good)
        {
            var t = TierOf(good, out bool neg);
            return t != null && !neg && TierNeedsNegative(t, chosen);
        }

        private static bool TierNeedsNegative(
            SkillTiers.Tier t, HashSet<string> chosen) =>
            t.negativeSkills.Any(sk => sk != null) && !HasGood(t, chosen)
                && !t.negativeSkills.Any(
                    sk => sk != null && chosen.Contains(sk.name));

        /// <summary>
        /// Why a good skill can't be taken now (null: it can), whatever
        /// negative skill goes with it.
        /// </summary>
        public static string CantTake(
            int level, HashSet<string> chosen, string good)
        {
            var t = TierOf(good, out bool neg);
            if (t == null || neg)
            {
                return "there's no such skill";
            }
            if (chosen.Contains(good))
            {
                return "you have this skill already";
            }
            if (t.level > level)
            {
                return $"this tier opens at level {t.level}";
            }
            if (GoodIn(t, chosen) >= 2)
            {
                return "this tier's two skills are taken";
            }
            if (Points(level, chosen) <= 0)
            {
                return "no skill points left: take a level at a stove";
            }
            return null;
        }

        /// <summary>
        /// Whether a negative skill can be taken now: its tier is open and
        /// still owes its negative skill (no good skill and no negative one
        /// taken there). good: the good skill it goes with, which must be of
        /// the same tier; null: any.
        /// </summary>
        public static string CantTakeNegative(
            int level, HashSet<string> chosen, string negative,
            string good = null)
        {
            var t = TierOf(negative, out bool neg);
            if (t == null || !neg)
            {
                return "there's no such negative skill";
            }
            if (chosen.Contains(negative))
            {
                return "you have this negative skill already";
            }
            if (t.level > level)
            {
                return $"this tier opens at level {t.level}";
            }
            if (HasGood(t, chosen))
            {
                return "this tier's negative skill was taken with its first "
                    + "skill";
            }
            if (!TierNeedsNegative(t, chosen))
            {
                return "this tier's negative skill is taken";
            }
            if (good != null && TierOf(good, out _) != t)
            {
                return "it goes with the first skill of its own tier";
            }
            return null;
        }

        /// <summary>
        /// Why this choice (a good skill, and a negative one or null) breaks
        /// the game's rules; null: it doesn't.
        /// </summary>
        public static string Check(
            int level, HashSet<string> chosen, string good, string negative)
        {
            string why = CantTake(level, chosen, good);
            if (why != null)
            {
                return why;
            }
            bool need = NeedsNegative(chosen, good);
            if (string.IsNullOrEmpty(negative))
            {
                if (need)
                {
                    return "a negative skill of its tier has to be taken with "
                        + "it";
                }
                return null;
            }
            if (!need)
            {
                return "no negative skill goes with this skill";
            }
            return CantTakeNegative(level, chosen, negative, good);
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
            if (p == null || p.skills == null || Tiers == null)
            {
                why = "not in a world";
                return false;
            }
            if (_waiting)
            {
                why = "waiting for the host";
                return false;
            }
            var chosen = Chosen(p);
            int level = p.currentLevel;
            why = Check(level, chosen, good, negative);
            if (why != null)
            {
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
                w.W.Write((byte)Mathf.Clamp(level, 0, 255));
                w.WriteStr(good);
                string sentNegative = negative;
                if (sentNegative == null)
                {
                    sentNegative = "";
                }
                w.WriteStr(sentNegative);
                var had = chosen.ToList();
                w.W.Write((byte)Math.Min(had.Count, 255));
                for (int i = 0; i < had.Count && i < 255; i++)
                {
                    w.WriteStr(had[i]);
                }
                Authority.Request(w, $"learn {good}", (ok, reason, _) =>
                {
                    _waiting = false;
                    if (ok)
                    {
                        return;
                    }
                    Take(gs);
                    Take(ns);
                    Plugin.Log.LogInfo(
                        $"[skills] the host refused {good}: {reason}; taken back");
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
            int level = r.R.ReadByte();
            string good = r.ReadStr(), negative = r.ReadStr();
            int n = r.R.ReadByte();
            var chosen = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                chosen.Add(r.ReadStr());
            }
            if (!S.IsHost || !Authority.Handling)
            {
                return;
            }
            if (Tiers == null)
            {
                Authority.Refuse("the host isn't in a world");
                return;
            }
            int most = Progression.MostLevels(Player.Instance);
            if (level > most)
            {
                Authority.Refuse(
                    $"level {level} is more than the shared essence gives ({most})");
                return;
            }
            string why = Check(level, chosen, good, negative);
            if (why != null)
            {
                Authority.Refuse(why);
                return;
            }
            string name = S.NameOf(from);
            string withNegative = "";
            if (negative.Length > 0)
            {
                withNegative = " with " + negative;
            }
            Plugin.Log.LogInfo(
                $"[skills] {name} learned {good}{withNegative} at level {level}");
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
                parts.Add(
                    $"tier {TierName(i)} (level {t.level}): {string.Join(", ", t.skills.Where(s => s != null).Select(s => s.name))}; " +
                          $"negative: {string.Join(", ", t.negativeSkills.Where(s => s != null).Select(s => s.name))}");
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
                bool good = HasGood(t, chosen),
                    owes = t.negativeSkills.Any(sk => sk != null);
                if (negs > 1 || (good && owes && negs != 1)
                    || (!good && negs > 0))
                {
                    wrong.Add(
                        $"tier {TierName(i)}: {negs} negative skills with {GoodIn(t, chosen)} skills");
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
            var chosen = Chosen(p);
            int level = p.currentLevel;
            foreach (var t in tiers)
            {
                foreach (var g in t.skills)
                {
                    if (g == null || CantTake(level, chosen, g.name) != null)
                    {
                        continue;
                    }
                    if (!NeedsNegative(chosen, g.name))
                    {
                        return new[] { g.name, null };
                    }
                    foreach (var ng in t.negativeSkills)
                    {
                        if (ng != null
                            && Check(level, chosen, g.name, ng.name) == null)
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
