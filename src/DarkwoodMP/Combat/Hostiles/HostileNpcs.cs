using System;
using DarkwoodMP.Combat.Hostiles;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// NPCs that turn on a player (gap 33). A friendly NPC is each player's
    /// own, as the rest of an NPC's story is. Once the host's copy turns
    /// hostile (a player hit it, a dialogue's outcome or a scripted attack sent
    /// it after one), the host runs it as one of its enemies (NetEnemies): its
    /// AI picks a player by threat over every player, the clients' copies are
    /// frozen and shown as the host's (found by save id, or for one a scripted
    /// event made, the one of that kind near the host's), hits on it from the
    /// clients are requests capped by their weapon, and its health, state and
    /// death are the host's. When it calms down (Character.calmDown: neutral
    /// and idle) for CalmTime, the host stops running it and each client's copy
    /// is its own again.
    ///
    /// A client's player turning a friendly NPC hostile doesn't do it in its
    /// own game: the hit goes to the host as NpcHit, a scripted
    /// "attack the player" as NpcAttack, and the host's copy turns on that
    /// player's stand-in.
    /// The host refuses one it hasn't got (an NPC only that client's game has,
    /// a hideout's own wolf or trader):
    /// that NPC is the client's, and the hit or attack happens there.
    ///
    /// The decisions are the core's HostileRules and NpcHitRules; the state
    /// is the session's NpcHostility (CoopSession.Hostiles), which NetEnemies,
    /// the patches and NpcDeaths call. The statics left here read a
    /// character for HostileRules; with no session nobody is running.
    /// </summary>
    public static class HostileNpcs
    {
        /// <summary>
        /// How long a host-run NPC stays calm before it's each player's own
        /// again.
        /// </summary>
        public const float CalmTime = HostileRules.CalmTime;

        /// <summary>
        /// Our player's hit on an NPC, as Character.getHit has it.
        /// </summary>
        internal struct Hit
        {
            public Character Npc;
            public float Damage;
            public Transform Attacker;
            public bool Cut;
            public bool ByPlayer;
            public bool Interrupt;
            public bool Normal;
        }

        /// <summary>
        /// What HostileRules reads of a character. Listed: the session's
        /// running set holds it.
        /// </summary>
        private static HostileFacts FactsOf(Character c)
        {
            if (c == null)
            {
                return new HostileFacts();
            }

            bool chasing = c.behaviour == Character.Behaviour.chasingTarget;
            bool onSight = c.aggressiveness == Aggressiveness.attackOnSight;
            if (c.npc == null)
            {
                // StillHostile's last two checks still count
                return new HostileFacts
                {
                    Chasing = chasing,
                    AttacksOnSight = onSight,
                };
            }

            bool listed = false;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                listed = coop.Hostiles.Listed(c);
            }

            bool inPlay = NetEnemies.InPlay(c);
            return new HostileFacts
            {
                IsNpc = true,
                InPlay = inPlay,
                Chasing = chasing,
                TargetIsPlayer = inPlay && chasing && IsPlayer(c.target),
                AttacksOnSight = onSight,
                AttacksPlayers = inPlay && onSight
                    && c.attacksFaction(Faction.player),
                Listed = listed,
            };
        }

        /// <summary>
        /// Whether the host runs this NPC as an enemy: on the host, one it runs
        /// or one that just turned on a player; on a client, a copy of one of
        /// those, or one of ours that turned hostile by itself (frozen and
        /// hidden until the host's shows, as our own enemies are).
        /// </summary>
        public static bool Running(Character c) =>
            HostileRules.Running(FactsOf(c));

        /// <summary>
        /// The NPC is after a player, or will attack one on sight.
        /// </summary>
        public static bool TurnedHostile(Character c) =>
            HostileRules.TurnedHostile(FactsOf(c));

        /// <summary>
        /// Host: still not calmed down (once it attacks on sight it stays so
        /// until Character.calmDown).
        /// </summary>
        public static bool StillHostile(Character c) =>
            HostileRules.StillHostile(FactsOf(c));

        private static bool IsPlayer(Transform t) => t != null
            && (t.GetComponent<Player>() != null
                || t.GetComponent<NetPlayerProxy>() != null);

        internal static string Who(Transform t)
        {
            if (t == null)
            {
                return "nobody";
            }

            var proxy = t.GetComponent<NetPlayerProxy>();
            if (proxy != null)
            {
                return proxy.OwnerName;
            }

            if (t.GetComponent<Player>() != null)
            {
                return "the host's player";
            }

            return t.name;
        }

        /// <summary>
        /// Host: send this NPC after a player's stand-in, as
        /// Character.attackPlayer does for our player.
        /// </summary>
        internal static void SendAfter(Character c, NetPlayerProxy proxy)
        {
            JoinerAi.GoFor(c, proxy.transform);
            Plugin.Log.LogInfo($"[npc] {c.name} sent after {proxy.OwnerName}");
        }
    }
}
