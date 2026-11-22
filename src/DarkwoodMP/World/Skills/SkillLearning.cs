using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Skills;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Learning skills (SkillTree): our own game learns at once, and a
    /// client asks the host (SkillSpend, a request), which checks the
    /// choice. One lives as long as the game (ModHost.Learning), since
    /// learning works playing alone too; each CoopSession handles the
    /// requests and, when it ends, forgets a choice still waiting for an
    /// answer (Forget).
    /// </summary>
    internal sealed class SkillLearning
    {
        private bool _waiting;

        /// <summary>
        /// Client: a choice is waiting for the host's answer.
        /// </summary>
        public bool Waiting => _waiting;

        /// <summary>The session ended: no answer is coming.</summary>
        public void Forget()
        {
            _waiting = false;
        }

        /// <summary>
        /// Take a good skill (and the negative one that must go with it, or
        /// null), as SkillsMenu.confirmSkills gives them.
        /// </summary>
        public bool Learn(string good, string negative, out string why)
        {
            var p = Player.Instance;
            why = null;
            var note = SkillRules.MayLearn(
                p != null && p.skills != null && SkillTree.Tiers != null,
                _waiting);
            if (note != SkillNote.None)
            {
                why = SkillNotes.Text(note);
                return false;
            }

            var choice = SkillTree.ChoiceOf(p) with
            {
                Good = good,
                Negative = negative,
            };

            var chosen = choice.Chosen;
            int level = choice.Level;
            var verdict = SkillRules.Check(choice);
            if (verdict.Refused)
            {
                why = Refusals.Text(verdict);
                return false;
            }

            var gs = SkillTree.Find(good);
            var ns = SkillTree.Find(negative);
            SkillTree.Give(gs, ns);
            string text = "";
            if (ns != null)
            {
                text = " with " + negative;
            }

            Plugin.Log.LogInfo(
                $"[skills] learned {good}{text}: {SkillTree.Points(p)} "
                    + "points left");
            CoopSession coop = Coop.Current;
            if (coop != null && NetEnemies.ClientMode && WorldShare.InWorld
                && coop.Net.IsClient)
            {
                _waiting = true;
                var w = coop.Net.Begin(MsgType.SkillSpend);
                SkillSpend.Write(
                    w,
                    new SpendRequest
                    {
                        Level = level,
                        Good = good,
                        Negative = negative,
                        Had = chosen.ToList(),
                    });

                coop.Authority.Request(
                    w, $"learn {good}", (ok, reason, _) =>
                {
                    _waiting = false;
                    if (ok)
                    {
                        return;
                    }

                    SkillTree.Take(gs);
                    SkillTree.Take(ns);
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

        /// <summary>
        /// Host: a client's SkillSpend, checked by SkillRules.MaySpend and
        /// refused with its verdict.
        /// </summary>
        public void OnSpend(ulong from, PacketReader r)
        {
            var spend = SkillSpend.Read(r);
            int level = spend.Level;
            string good = spend.Good, negative = spend.Negative;
            var chosen = new HashSet<string>(spend.Had);

            CoopSession coop = Coop.Current;
            var authority = coop.Authority;
            if (!SkillRules.Handles(coop.Net.IsHost, authority.Handling))
            {
                return;
            }

            var tiers = SkillTree.Facts;
            int most = 0;
            if (tiers != null)
            {
                most = Plugin.Instance.Host.Progress.MostLevels(
                    Player.Instance);
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

            string name = coop.Net.NameOf(from);
            string withNegative = "";
            if (negative.Length > 0)
            {
                withNegative = " with " + negative;
            }

            Plugin.Log.LogInfo(
                $"[skills] {name} learned {good}{withNegative} at level "
                    + $"{level}");
        }
    }
}
