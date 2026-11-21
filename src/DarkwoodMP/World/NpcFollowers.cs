using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Npcs;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// An NPC that follows a player (aggressiveness "follower", behaviour
    /// "following") follows whoever its sight check gives it as its target.
    /// NPCs run in every game and clients keep their copies where the host has
    /// them (NpcSync), so the host's copy decides where it goes, and on the
    /// host a friendly NPC sees only the host's own player
    /// (Threat.BeforeSightCheck). Without more, a follower a joiner recruited
    /// would follow the host.
    ///
    /// So the host remembers who recruited each follower: the joiner whose
    /// event (ScriptedEvents.By, from their EventFired) made it a follower, by
    /// a modifyCharacter step that sets aggressiveness follower or behaviour
    /// following, or by a gameObject step that switches on a character that is
    /// a follower. On the host such a follower sees only that joiner's stand-in
    /// (NetPlayerProxy), and is pointed at it whenever it has none or the
    /// host's player as its target. Followers the host's own player recruited,
    /// or that were followers from the start, follow the host's player as
    /// before. The record isn't saved: after the host reloads, or once the
    /// recruiter has left, the follower goes back to following the host's
    /// player.
    /// </summary>
    internal sealed class NpcFollowers
    {
        /// <summary>
        /// Host: follower -> the peer id of the joiner who recruited it.
        /// </summary>
        private readonly Dictionary<Character, ulong> _recruiter =
            new Dictionary<Character, ulong>();

        public NpcFollowers(CoopSession coop)
        {
            Session net = coop.Net;
            net.OnPeerDisconnected += Forget;
            coop.Hook(() => net.OnPeerDisconnected -= Forget);
        }

        /// <summary>
        /// A joiner left: its followers go back to following the host's
        /// player.
        /// </summary>
        private void Forget(ulong id)
        {
            var gone = new List<Character>();
            foreach (var kv in _recruiter)
            {
                if (kv.Value == id)
                {
                    gone.Add(kv.Key);
                }
            }

            foreach (var c in gone)
            {
                _recruiter.Remove(c);
            }
        }

        /// <summary>
        /// Host: note that this joiner recruited this character, if it's a
        /// follower now or about to be.
        /// </summary>
        internal void Recruited(GameObject go, ulong by)
        {
            Character c = null;
            if (go != null)
            {
                c = go.GetComponent<Character>();
            }

            if (c == null || c.npc == null)
            {
                return;
            }

            _recruiter[c] = by;
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] {c.name} follows {NameOf(by)}, who recruited it");
            }
        }

        private static string NameOf(ulong id)
        {
            if (NetPlayerProxy.All.TryGetValue(id, out var p) && p != null)
            {
                return p.OwnerName;
            }

            return id.ToString();
        }

        /// <summary>
        /// Host: the stand-in of the joiner a follower follows, or null (it
        /// follows the host's player).
        /// </summary>
        public NetPlayerProxy LeaderOf(Character c)
        {
            if (c == null || _recruiter.Count == 0)
            {
                return null;
            }

            FollowerFacts facts = FactsOf(c, out NetPlayerProxy leader);
            if (!FollowerRules.FollowsLeader(facts))
            {
                return null;
            }

            return leader;
        }

        /// <summary>
        /// The facts of a character that may follow a joiner, and that
        /// joiner's stand-in (null with none, or not recorded).
        /// </summary>
        private FollowerFacts FactsOf(
            Character c,
            out NetPlayerProxy leader)
        {
            leader = null;
            bool recruited = _recruiter.TryGetValue(c, out ulong id);
            if (recruited
                && NetPlayerProxy.All.TryGetValue(id, out var p) && p != null)
            {
                leader = p;
            }

            return new FollowerFacts
            {
                IsNpc = c.npc != null,
                IsFollower = c.aggressiveness == Aggressiveness.follower,
                IsFollowing = c.behaviour == Character.Behaviour.following,
                Recruited = recruited,
                LeaderAlive = leader != null && leader.alive,
            };
        }

        /// <summary>
        /// Host, before a friendly NPC's sight check: a recruited follower sees
        /// only its leader among the players, and goes after it. Returns false
        /// if it isn't one (the caller does as for any friendly NPC).
        /// </summary>
        public bool BeforeSightCheck(Character c)
        {
            var leader = LeaderOf(c);
            if (leader == null)
            {
                return false;
            }

            c.charactersInViewRange.RemoveAll(
                col => FollowerRules.Hides(SeenOf(col, leader)));
            if (FollowerRules.Retargets(TargetOf(c, leader)))
            {
                c.target = leader.transform;
            }

            return true;
        }

        /// <summary>
        /// What a follower with a leader sees: a collider in its view range.
        /// </summary>
        private static SeenFacts SeenOf(Collider col, NetPlayerProxy leader)
        {
            CharBase cb = null;
            if (col != null)
            {
                cb = col.GetComponent<CharBase>();
            }

            if (cb == null)
            {
                return new SeenFacts();
            }

            return new SeenFacts
            {
                Exists = true,
                IsLeader = cb == leader,
                IsPlayer = Proxies.IsPlayer(cb),
            };
        }

        /// <summary>
        /// What a follower with a leader has as its target.
        /// </summary>
        private static SeenFacts TargetOf(Character c, NetPlayerProxy leader)
        {
            if (c.target == null)
            {
                return new SeenFacts();
            }

            if (c.target == leader.transform)
            {
                return new SeenFacts { Exists = true, IsLeader = true };
            }

            return new SeenFacts
            {
                Exists = true,
                IsPlayer = Proxies.IsPlayer(
                    c.target.GetComponent<CharBase>()),
            };
        }

        /// <summary>
        /// A scripted step as it starts, by the peer whose event it is.
        /// </summary>
        internal static StepFacts FactsOf(GameEvent ge, ulong by)
        {
            Session session = Session.Instance;
            return new StepFacts
            {
                Hosting = session != null && session.IsHost,
                By = by,
                ModifiesCharacter =
                    ge.type == GameEvent.Type.modifyCharacter,
                SwitchesObject = ge.type == GameEvent.Type.gameObject,
                SetsAggressiveness = ge.characterModifyType
                    == GameEvent.CharacterModify.aggressiveness,
                ToFollower = ge.destAggressiveness == Aggressiveness.follower,
                SetsBehaviour = ge.characterModifyType
                    == GameEvent.CharacterModify.behaviour,
                ToFollowing = ge.destCharacterBehaviour
                    == Character.Behaviour.following,
            };
        }

        /// <summary>
        /// A target of a recruiting step, as one that may be recruited.
        /// </summary>
        internal static FollowerFacts TargetFactsOf(Character c)
        {
            if (c == null)
            {
                return new FollowerFacts();
            }

            return new FollowerFacts
            {
                IsNpc = c.npc != null,
                IsFollower = c.aggressiveness == Aggressiveness.follower,
            };
        }

        internal static IEnumerable<GameObject> Targets(GameEvent ge)
        {
            foreach (var go in ge.targetGameObjects)
            {
                if (go != null)
                {
                    yield return go;
                }
            }

            var uo = Singleton<UniqueObjects>.Instance;
            if (uo == null)
            {
                yield break;
            }

            foreach (var name in ge.targetUniqueObjects)
            {
                var go = uo.getObject(name);
                if (go != null)
                {
                    yield return go;
                }
            }
        }
    }

    /// <summary>
    /// Host: a joiner's event step that makes characters followers records the
    /// joiner as their recruiter.
    /// </summary>
    [HarmonyPatch(typeof(GameEvent), nameof(GameEvent.fire))]
    static class FollowerRecruitPatch
    {
        static void Prefix(GameEvent __instance)
        {
            if (__instance == null)
            {
                return;
            }

            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            ulong by = ScriptedEvents.By;
            StepFacts step = NpcFollowers.FactsOf(__instance, by);
            if (!FollowerRules.Recruits(step))
            {
                return;
            }

            foreach (var go in NpcFollowers.Targets(__instance))
            {
                var c = go.GetComponent<Character>();
                FollowerFacts target = NpcFollowers.TargetFactsOf(c);
                if (FollowerRules.Counts(step, target))
                {
                    coop.Followers.Recruited(go, step.By);
                }
            }
        }
    }
}
