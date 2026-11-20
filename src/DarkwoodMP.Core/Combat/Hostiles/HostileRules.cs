using System.Collections.Generic;
using DarkwoodMP.World;

namespace DarkwoodMP.Combat.Hostiles
{
    /// NPCs that turn on a player (the shell's HostileNpcs). Once the
    /// host's copy turns hostile, the host runs it as one of its enemies;
    /// when it calms down for CalmTime, each player's copy is their own
    /// again. The host refuses a hit or an attack on an NPC it hasn't got,
    /// and the client then acts on its own copy.
    public static class HostileRules
    {
        /// How long a host-run NPC stays calm before it is each player's
        /// own again.
        public const float CalmTime = 8f;

        /// How far from the host's NPC our copy of it may be, in 3D
        /// (HostileNpcs.OurCopy).
        public const float CopyRange = 3000f;

        /// How far from the NPC the requester of an NpcAttack may be
        /// (NetEnemies.RelevantRange).
        public const float AttackReach = 3000f;

        /// The NPC is after a player, or will attack one on sight.
        public static bool TurnedHostile(in HostileFacts npc)
        {
            if (!npc.IsNpc || !npc.InPlay)
            {
                return false;
            }

            if (npc.Chasing && npc.TargetIsPlayer)
            {
                return true;
            }

            return npc.AttacksOnSight && npc.AttacksPlayers;
        }

        /// Host: not calmed down yet. Once it attacks on sight it stays so
        /// until Character.calmDown. The last two checks don't ask whether
        /// it is an NPC or in play.
        public static bool StillHostile(in HostileFacts npc)
        {
            return TurnedHostile(npc) || npc.AttacksOnSight || npc.Chasing;
        }

        /// Whether the host runs this NPC as an enemy: one in the running
        /// set, or one that just turned on a player.
        public static bool Running(in HostileFacts npc)
        {
            if (!npc.IsNpc)
            {
                return false;
            }

            return npc.Listed || TurnedHostile(npc);
        }

        /// Host: whether the NPC a client's NpcHit or NpcAttack names is
        /// the host's to run. NoSuchNpc tells the client to act on its own
        /// copy (it compares the words).
        public static Verdict Resolve(in NpcAskFacts ask)
        {
            if (!ask.Found || !ask.IsNpc)
            {
                return Verdict.Refuse(Refusal.NoSuchNpc);
            }

            // Dead here (each player's NPC story is their own): there's
            // nothing for us to run, so it's the client's.
            if (!ask.InPlay)
            {
                return Verdict.Refuse(Refusal.NoSuchNpc);
            }

            if (!ask.HasStandIn)
            {
                return Verdict.Refuse(Refusal.NoStandIn);
            }

            return Verdict.Allow;
        }

        /// Host: a client's NpcAttack: Resolve, then the requester within
        /// AttackReach of the NPC (Authority.InReach, with the NPC's name).
        public static Verdict MayAttack(
            in NpcAskFacts ask,
            in ReachFacts reach)
        {
            var verdict = Resolve(ask);
            if (verdict.Refused)
            {
                return verdict;
            }

            return ReachRules.InReach(reach);
        }

        /// Client: which of our characters stands in for the host's NPC of
        /// this kind at pos, when its save id didn't name one: the nearest
        /// in 3D under CopyRange that is an NPC (or died here as one),
        /// isn't driven and is of that kind. A tie goes to the first. -1
        /// when none.
        public static int OurCopy(
            IReadOnlyList<HostileCandidate> ours,
            string kind,
            Vec3 pos)
        {
            int best = -1;
            float bestD = CopyRange * CopyRange;
            for (int i = 0; i < ours.Count; i++)
            {
                var c = ours[i];
                bool npc = c.IsNpc || c.DiedHere;
                if (!npc || c.Driven || c.Kind != kind)
                {
                    continue;
                }

                float d = SqrDistance(c.Pos, pos);
                if (d < bestD)
                {
                    best = i;
                    bestD = d;
                }
            }

            return best;
        }

        /// Client: whether effects activating on this NPC now are those
        /// of a hit just left to the host.
        public static bool EffectsForwarded(in ForwardFacts hit)
        {
            return !hit.Ours
                && hit.Forwarded
                && hit.Frame == hit.ForwardedFrame;
        }

        /// As (a - b).sqrMagnitude.
        static float SqrDistance(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return x * x + y * y + z * z;
        }
    }
}
