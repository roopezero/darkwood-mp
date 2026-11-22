using System.Collections.Generic;

namespace DarkwoodMP.World.Npcs
{
    /// NPC deaths: which NPC a death names (the plugin's
    /// NpcDeaths.Dying), and the host's check of a client's NpcDied
    /// (WorldActions.Allowed).
    public static class NpcDeathRules
    {
        /// How far from an NPC a player may be to say it died.
        public const float DeathReach = 2500f;

        /// How far from where the death says it stood a living NPC of that
        /// name may be, on the ground plane.
        public const float FindRadius = 400f;

        /// The answer of Pick that means the ObjRef's own result.
        public const int ByRef = -1;

        /// Which NPC a death names: ByRef, or the index of a candidate.
        /// The ObjRef's NPC when it has the name, or no name is given. Else
        /// the living candidate of that name nearest to where it stood,
        /// within FindRadius. With none, the ObjRef's result again, even
        /// when its name differs.
        public static int Pick(
            in DyingFacts death,
            IReadOnlyList<NpcCandidate> npcs)
        {
            bool unnamed = string.IsNullOrEmpty(death.Name);
            if (death.RefIsNpc && (unnamed || death.RefName == death.Name))
            {
                return ByRef;
            }

            if (unnamed)
            {
                return ByRef;
            }

            int best = ByRef;
            float bestSq = FindRadius * FindRadius;
            for (int i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];
                if (npc.Name != death.Name)
                {
                    continue;
                }

                if (!npc.HasCharacter || !npc.Alive)
                {
                    continue;
                }

                float dx = npc.At.X - death.At.X;
                float dz = npc.At.Z - death.At.Z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }

            return best;
        }

        /// Whether a game sends NpcDied for one of its characters'
        /// deaths: it has an NPC and was alive before Character.die, at the
        /// outermost call, while sharing. It doesn't ask whether the
        /// character is dead after the call, as the plugin didn't.
        public static bool SendsDeath(in DeathSentFacts npc)
        {
            if (!npc.Outermost || !npc.Sharing)
            {
                return false;
            }

            return npc.HasNpc && npc.WasAlive;
        }

        /// Host: whether a client's player could have killed this NPC: one
        /// was found, within DeathReach. On a yes the shell also tells
        /// FlagsSync the death is allowed.
        public static Verdict MayDie(in NpcDeathFacts npc)
        {
            if (!npc.Found)
            {
                return Verdict.Refuse(Refusal.NoSuchNpc);
            }

            return ReachRules.InReach(
                npc.Requester with { Reach = DeathReach });
        }
    }
}
