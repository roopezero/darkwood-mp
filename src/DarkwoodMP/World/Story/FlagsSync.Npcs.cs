using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// FlagsSync's reputations and NPC states: the after-night rewards, the
    /// host's check of a joiner's dead NPC, and the changes every game makes
    /// by itself.
    /// </summary>
    internal sealed partial class FlagsSync
    {
        /// <summary>
        /// The after-night reward each trader got.
        /// </summary>
        private readonly TraderRewards _rewards = new TraderRewards();

        /// <summary>
        /// Change this NPC's reputation here, without sending the change.
        /// </summary>
        internal void AddReputation(string npc, int delta)
        {
            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(npc);
            }

            if (st == null || delta == 0)
            {
                return;
            }

            st.reputation += delta;
            Absorb(npc, delta);
            StoryApply.RefreshReputation(npc);
        }

        /// <summary>
        /// This game gave a trader the after-night reward
        /// (Controller.startAfterNight; already absorbed). It counts once a
        /// day: if another game's for today came first, ours is taken back;
        /// otherwise it goes to the others.
        /// </summary>
        internal void LocalReward(string trader, int day, int amount)
        {
            if (!Sharing)
            {
                return;
            }

            var got = _rewards.Local(trader, day, amount);
            if (got.Kind == RewardKind.Ignored)
            {
                return;
            }

            if (got.Kind == RewardKind.TakenBack)
            {
                AddReputation(trader, got.Change);
                Plugin.Log.LogInfo(
                    $"[world] {trader} already got day {day}'s after-night "
                        + $"reward ({got.Had}) in another game: ours "
                        + $"({amount}) taken back");
                return;
            }

            Plugin.Log.LogInfo(
                $"[world] {trader}'s after-night reward for day {day}: "
                    + $"{amount}, sent to the others");
            Send(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    StoryKeys.Reward + trader,
                    StoryKeys.RewardValue(day, amount))
            }, except: 0);
        }

        /// <summary>
        /// Another game's after-night reward for a trader. Returns whether the
        /// host passes it on.
        /// </summary>
        private bool ApplyReward(string trader, string value)
        {
            StoryKeys.ReadReward(value, out int day, out int amount);
            RewardOutcome got;
            if (Net.IsHost)
            {
                got = _rewards.TakeOnHost(trader, day, amount);
            }
            else
            {
                // the host's is the one that counts
                got = _rewards.TakeOnClient(trader, day, amount);
            }

            if (got.Kind == RewardKind.Refused)
            {
                Plugin.Log.LogInfo(
                    $"[world] {trader} already got day {day}'s after-night "
                        + $"reward: another one ({amount}) not given");
                return false;
            }

            AddReputation(trader, got.Change);
            Plugin.Log.LogInfo(
                $"[world] {trader}'s after-night reward for day {day}: "
                    + $"{amount}");
            return true;
        }

        /// <summary>
        /// Host: NPCs whose death a joiner's NpcDied showed us just now
        /// (WorldActions), and when.
        /// </summary>
        private readonly AllowedKills _deathsAllowed = new AllowedKills();

        /// <summary>
        /// Host: a joiner killed this NPC and we checked it (their NpcDied
        /// request was allowed).
        /// </summary>
        internal void DeathAllowed(string npc)
        {
            _deathsAllowed.Allow(npc, new Seconds(Time.unscaledTime));
        }

        /// <summary>
        /// Host: checks a joiner's NPC state against our NPC's death. A
        /// player's kill reaches us first as an NpcDied request, which we check
        /// (reach, the NPC here) and which kills our copy; a dead state that
        /// didn't come that way isn't taken (or any client could kill any NPC
        /// everywhere). Nothing in the game brings a dead NPC back, so a state
        /// that says alive over our dead one is out of date (sent before our
        /// death reached them) and isn't taken either. Either way the sender
        /// gets our state back.
        /// </summary>
        private bool DeathOk(ulong from, string npc, string value)
        {
            var st = Singleton<Flags>.Instance.getNPCState(npc);
            switch (NpcStateRules.Judge(FactsOf(st, npc, value)))
            {
                case NpcStateOutcome.Stale:
                    SendNpcState(from, npc, st);
                    return false;
                case NpcStateOutcome.NotAllowed:
                    Plugin.Log.LogInfo(
                        $"[authority] refused {Net.NameOf(from)}'s "
                            + $"state of {npc}: dead, but no kill of theirs "
                            + "was allowed here");
                    SendNpcState(from, npc, st);
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// What the host knows of a joiner's state of an NPC: its "dead"
        /// (unreadable: Apply reports it), the host's own state, and whether
        /// a kill of theirs was allowed just now.
        /// </summary>
        private NpcStateFacts FactsOf(
            Flags.NPCState ours, string npc, string value)
        {
            bool readable = true;
            bool dead = false;
            try
            {
                bool? saved = JObject.Parse(value).Value<bool?>("dead");
                if (saved.HasValue)
                {
                    dead = saved.Value;
                }
            }
            catch
            {
                readable = false;
            }

            bool allowed = false;
            if (readable && dead)
            {
                allowed = _deathsAllowed.Recent(
                    npc, new Seconds(Time.unscaledTime));
            }

            return new NpcStateFacts
            {
                Readable = readable,
                TheirsDead = dead,
                OursKnown = ours != null,
                OursDead = ours != null && ours.dead,
                KillAllowed = allowed,
            };
        }

        private void SendNpcState(ulong to, string npc, Flags.NPCState st)
        {
            if (st == null || st.name == null)
            {
                return;
            }

            var w = Net.Begin(MsgType.FlagsDelta);
            string json = StoryRead.NpcJson(st);
            FlagsDelta.Write(w, new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("n:" + npc, json)
            });

            Net.SendTo(to, w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Another game's reputation change ("+d"), or a reputation sent whole
        /// ("=v").
        /// </summary>
        private void ApplyReputation(string npc, string value)
        {
            var st = Singleton<Flags>.Instance.getNPCState(npc);
            if (st == null)
            {
                return;
            }

            int v = int.Parse(value.Substring(1));
            if (value[0] != '=')
            {
                AddReputation(npc, v);
                return;
            }

            st.reputation = v;
            if (_baseline != null)
            {
                _baseline["r:" + npc] = v.ToString();
            }

            StoryApply.RefreshReputation(npc);
        }

        /// <summary>
        /// A scripted event's step is about to run: the reputation a
        /// reputation step (GameEvent modifyCharacter / reputation) changes,
        /// or int.MinValue for any other step (or no such NPC).
        /// </summary>
        internal int ReputationBefore(GameEvent ev)
        {
            if (ev == null || ev.type != GameEvent.Type.modifyCharacter
                || ev.characterModifyType
                    != GameEvent.CharacterModify.reputation)
            {
                return int.MinValue;
            }

            var st = NpcState(ev.Value);
            if (st == null)
            {
                return int.MinValue;
            }

            return st.reputation;
        }

        /// <summary>
        /// A scripted event's reputation step ran. It is a shared step: it
        /// runs in every game (ScriptedEvents), so each game's change is its
        /// own and isn't sent as a change too.
        /// </summary>
        internal void StepRan(GameEvent ev, int before)
        {
            var st = NpcState(ev.Value);
            if (st != null && st.reputation != before)
            {
                Absorb(ev.Value, st.reputation - before);
            }
        }

        private static Flags.NPCState NpcState(string npc)
        {
            Flags instance = Singleton<Flags>.Instance;
            if (instance == null)
            {
                return null;
            }

            return instance.getNPCState(npc);
        }
    }
}
