using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// An NPC that follows a player (aggressiveness "follower", behaviour "following") follows whoever its sight
    /// check gives it as its target. NPCs run in every game and clients keep their copies where the host has them
    /// (NpcSync), so the host's copy decides where it goes, and on the host a friendly NPC sees only the host's own
    /// player (Threat.BeforeSightCheck). Without more, a follower a joiner recruited would follow the host.
    ///
    /// So the host remembers who recruited each follower: the joiner whose event (ScriptedEvents.By, from their
    /// EventFired) made it a follower, by a modifyCharacter step that sets aggressiveness follower or behaviour
    /// following, or by a gameObject step that switches on a character that is a follower. On the host such a
    /// follower sees only that joiner's stand-in (NetPlayerProxy), and is pointed at it whenever it has none or the
    /// host's player as its target. Followers the host's own player recruited, or that were followers from the start,
    /// follow the host's player as before. The record isn't saved: after the host reloads, or once the recruiter has
    /// left, the follower goes back to following the host's player.
    /// </summary>
    public static class NpcFollowers
    {
        /// <summary>Host: follower -> the peer id of the joiner who recruited it.</summary>
        private static readonly Dictionary<Character, ulong> Recruiter = new Dictionary<Character, ulong>();
        private static bool _hooked;

        private static void Hook()
        {
            if (_hooked || Session.Instance == null) return;
            _hooked = true;
            Session.Instance.OnSessionEnded += () => Recruiter.Clear();
            Session.Instance.OnPeerDisconnected += id =>
            {
                var gone = new List<Character>();
                foreach (var kv in Recruiter) if (kv.Value == id) gone.Add(kv.Key);
                foreach (var c in gone) Recruiter.Remove(c);
            };
        }

        /// <summary>Host: note that this joiner recruited this character, if it's a follower now or about to be.</summary>
        internal static void Recruited(GameObject go, ulong by)
        {
            var c = go != null ? go.GetComponent<Character>() : null;
            if (c == null || c.npc == null) return;
            Hook();
            Recruiter[c] = by;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {c.name} follows {NameOf(by)}, who recruited it");
        }

        private static string NameOf(ulong id) =>
            NetPlayerProxy.All.TryGetValue(id, out var p) && p != null ? p.OwnerName : id.ToString();

        /// <summary>Host: the stand-in of the joiner a follower follows, or null (it follows the host's player).</summary>
        public static NetPlayerProxy LeaderOf(Character c)
        {
            if (c == null || Recruiter.Count == 0 || !Recruiter.TryGetValue(c, out var id)) return null;
            if (c.aggressiveness != Aggressiveness.follower && c.behaviour != Character.Behaviour.following) return null;
            return NetPlayerProxy.All.TryGetValue(id, out var p) && p != null && p.alive ? p : null;
        }

        /// <summary>
        /// Host, before a friendly NPC's sight check: a recruited follower sees only its leader among the players, and
        /// goes after it. Returns false if it isn't one (the caller does as for any friendly NPC).
        /// </summary>
        public static bool BeforeSightCheck(Character c)
        {
            var leader = LeaderOf(c);
            if (leader == null) return false;
            c.charactersInViewRange.RemoveAll(col =>
            {
                var cb = col != null ? col.GetComponent<CharBase>() : null;
                return cb != null && cb != leader && Proxies.IsPlayer(cb);
            });
            if (c.target == null || c.target != leader.transform && Proxies.IsPlayer(c.target.GetComponent<CharBase>()))
                c.target = leader.transform;
            return true;
        }

        /// <summary>Whether a scripted step makes its targets followers.</summary>
        internal static bool MakesFollowers(GameEvent ge)
        {
            if (ge.type == GameEvent.Type.modifyCharacter)
                return ge.characterModifyType == GameEvent.CharacterModify.aggressiveness && ge.destAggressiveness == Aggressiveness.follower
                    || ge.characterModifyType == GameEvent.CharacterModify.behaviour && ge.destCharacterBehaviour == Character.Behaviour.following;
            return ge.type == GameEvent.Type.gameObject;
        }

        internal static IEnumerable<GameObject> Targets(GameEvent ge)
        {
            foreach (var go in ge.targetGameObjects) if (go != null) yield return go;
            var uo = Singleton<UniqueObjects>.Instance;
            if (uo == null) yield break;
            foreach (var name in ge.targetUniqueObjects)
            {
                var go = uo.getObject(name);
                if (go != null) yield return go;
            }
        }
    }

    /// <summary>Host: a joiner's event step that makes characters followers records the joiner as their recruiter.</summary>
    [HarmonyPatch(typeof(GameEvent), nameof(GameEvent.fire))]
    static class FollowerRecruitPatch
    {
        static void Prefix(GameEvent __instance)
        {
            var by = ScriptedEvents.By;
            if (by == 0 || __instance == null || Session.Instance == null || !Session.Instance.IsHost) return;
            if (!NpcFollowers.MakesFollowers(__instance)) return;
            bool gameObjectStep = __instance.type == GameEvent.Type.gameObject;
            foreach (var go in NpcFollowers.Targets(__instance))
            {
                var c = go.GetComponent<Character>();
                if (c == null || c.npc == null) continue;
                // A gameObject step counts only for a character that is a follower already (switched on to follow).
                if (gameObjectStep && c.aggressiveness != Aggressiveness.follower) continue;
                NpcFollowers.Recruited(go, by);
            }
        }
    }
}
