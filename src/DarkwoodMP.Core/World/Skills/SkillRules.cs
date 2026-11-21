using System.Collections.Generic;

namespace DarkwoodMP.World.Skills
{
    /// The game's rules for spending a skill point (SkillTree): a tier opens
    /// at its level, takes two good skills at most, and a tier with negative
    /// skills gives one of them with its first good skill. Each level is a
    /// point; the good skills a player has are the points spent.
    public static class SkillRules
    {
        /// The most good skills one tier gives (Tier.locked).
        public const int MostGoodPerTier = 2;

        static readonly string[] TierNames = { "I", "II", "III", "IV" };

        /// A skill by name: the tiers in order, each tier's good skills
        /// before its negative ones, and the first name that matches. None
        /// for a null or empty name, or with no tiers.
        public static SkillSpot Find(TierFacts[] tiers, string name)
        {
            if (tiers == null || string.IsNullOrEmpty(name))
            {
                return SkillSpot.None;
            }

            for (int i = 0; i < tiers.Length; i++)
            {
                if (Has(tiers[i].Good, name))
                {
                    return new SkillSpot(i, false);
                }

                if (Has(tiers[i].Negative, name))
                {
                    return new SkillSpot(i, true);
                }
            }

            return SkillSpot.None;
        }

        /// The tier's name as the game shows it: I to IV, then its number.
        public static string TierName(int index)
        {
            if (index >= 0 && index < TierNames.Length)
            {
                return TierNames[index];
            }

            return (index + 1).ToString();
        }

        /// Points to spend: the level, less the good skills had, never
        /// below zero.
        public static int Points(
            TierFacts[] tiers,
            int level,
            HashSet<string> chosen)
        {
            int good = 0;
            if (chosen != null)
            {
                foreach (var name in chosen)
                {
                    var spot = Find(tiers, name);
                    if (spot.Found && !spot.Negative)
                    {
                        good++;
                    }
                }
            }

            int left = level - good;
            if (left > 0)
            {
                return left;
            }

            return 0;
        }

        /// Whether this good skill must come with a negative skill: its tier
        /// has negative skills and none of its skills is taken yet.
        public static bool NeedsNegative(
            TierFacts[] tiers,
            HashSet<string> chosen,
            string good)
        {
            var spot = Find(tiers, good);
            if (!spot.Found || spot.Negative)
            {
                return false;
            }

            return TierNeedsNegative(tiers[spot.Tier], chosen);
        }

        /// Whether the good skill can be taken now, whatever negative skill
        /// goes with it (SkillTree.CantTake). Uses Good, not Negative.
        public static Verdict MayTake(in ChoiceFacts choice)
        {
            var spot = Find(choice.Tiers, choice.Good);
            if (!spot.Found || spot.Negative)
            {
                return Verdict.Refuse(Refusal.NoSuchSkill);
            }

            if (Contains(choice.Chosen, choice.Good))
            {
                return Verdict.Refuse(Refusal.HaveSkill);
            }

            var tier = choice.Tiers[spot.Tier];
            if (tier.Level > choice.Level)
            {
                return Verdict.Refuse(
                    Refusal.TierClosed,
                    tier.Level.ToString());
            }

            if (CountIn(tier.Good, choice.Chosen) >= MostGoodPerTier)
            {
                return Verdict.Refuse(Refusal.TierFull);
            }

            if (Points(choice.Tiers, choice.Level, choice.Chosen) <= 0)
            {
                return Verdict.Refuse(Refusal.NoSkillPoints);
            }

            return Verdict.Allow;
        }

        /// Whether the negative skill can be taken now: its tier is open
        /// and still owes its negative skill (SkillTree.CantTakeNegative).
        /// Good, when not null, is the good skill it goes with, which must
        /// be in the same tier; null allows any.
        public static Verdict MayTakeNegative(in ChoiceFacts choice)
        {
            var spot = Find(choice.Tiers, choice.Negative);
            if (!spot.Found || !spot.Negative)
            {
                return Verdict.Refuse(Refusal.NoSuchNegative);
            }

            if (Contains(choice.Chosen, choice.Negative))
            {
                return Verdict.Refuse(Refusal.HaveNegative);
            }

            var tier = choice.Tiers[spot.Tier];
            if (tier.Level > choice.Level)
            {
                return Verdict.Refuse(
                    Refusal.TierClosed,
                    tier.Level.ToString());
            }

            if (CountIn(tier.Good, choice.Chosen) > 0)
            {
                return Verdict.Refuse(Refusal.NegativeWithFirst);
            }

            if (!TierNeedsNegative(tier, choice.Chosen))
            {
                return Verdict.Refuse(Refusal.NegativeTaken);
            }

            if (choice.Good != null)
            {
                var good = Find(choice.Tiers, choice.Good);
                if (!good.Found || good.Tier != spot.Tier)
                {
                    return Verdict.Refuse(Refusal.NegativeOtherTier);
                }
            }

            return Verdict.Allow;
        }

        /// Whether this choice (a good skill, and a negative one or none)
        /// keeps the game's rules (SkillTree.Check).
        public static Verdict Check(in ChoiceFacts choice)
        {
            var take = MayTake(choice);
            if (take.Refused)
            {
                return take;
            }

            bool need = NeedsNegative(
                choice.Tiers,
                choice.Chosen,
                choice.Good);
            if (string.IsNullOrEmpty(choice.Negative))
            {
                if (need)
                {
                    return Verdict.Refuse(Refusal.NeedsNegative);
                }

                return Verdict.Allow;
            }

            if (!need)
            {
                return Verdict.Refuse(Refusal.NoNegativeNeeded);
            }

            return MayTakeNegative(choice);
        }

        /// Whether our own game may learn now (SkillTree.Learn's first two
        /// checks, before Check): in a world, with the player's skills and
        /// the tiers, and no choice waiting for the host's answer.
        public static SkillNote MayLearn(bool inWorld, bool waiting)
        {
            if (!inWorld)
            {
                return SkillNote.NotInWorld;
            }

            if (waiting)
            {
                return SkillNote.Waiting;
            }

            return SkillNote.None;
        }

        /// Whether the host looks at a client's SkillSpend: only as the
        /// host, inside the request (Authority.Handling); otherwise the
        /// handler returns without an answer.
        public static bool Handles(bool isHost, bool handling)
        {
            return isHost && handling;
        }

        /// The host's check of a client's SkillSpend (SkillTree.OnSpend):
        /// the host's tiers, then the level against the shared essence,
        /// then the game's rules. The client's list of skills is its word.
        public static Verdict MaySpend(in SpendFacts spend)
        {
            if (spend.Choice.Tiers == null)
            {
                return Verdict.Refuse(Refusal.HostNotInWorld);
            }

            if (spend.Choice.Level > spend.Most)
            {
                return Verdict.Refuse(
                    Refusal.LevelTooHigh,
                    Refusals.Detail(
                        spend.Choice.Level.ToString(),
                        spend.Most.ToString()));
            }

            return Check(spend.Choice);
        }

        static bool TierNeedsNegative(TierFacts tier, HashSet<string> chosen)
        {
            return Length(tier.Negative) > 0
                && CountIn(tier.Good, chosen) == 0
                && CountIn(tier.Negative, chosen) == 0;
        }

        static int CountIn(string[] names, HashSet<string> chosen)
        {
            int n = 0;
            if (names == null)
            {
                return n;
            }

            foreach (var name in names)
            {
                if (Contains(chosen, name))
                {
                    n++;
                }
            }

            return n;
        }

        static bool Has(string[] names, string name)
        {
            if (names == null)
            {
                return false;
            }

            foreach (var each in names)
            {
                if (each == name)
                {
                    return true;
                }
            }

            return false;
        }

        static bool Contains(HashSet<string> chosen, string name)
        {
            return chosen != null && name != null && chosen.Contains(name);
        }

        static int Length(string[] names)
        {
            if (names == null)
            {
                return 0;
            }

            return names.Length;
        }
    }
}
