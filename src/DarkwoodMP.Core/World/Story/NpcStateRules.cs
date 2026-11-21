namespace DarkwoodMP.World.Story
{
    /// The host's check of a joiner's NPC state against its own NPC's
    /// death, and which NPCs die when another game's state says one did.
    public static class NpcStateRules
    {
        /// Seconds after an allowed NpcDied request in which a dead state
        /// for that NPC is taken (strictly less, unscaled time).
        public const float KillWindow = 30f;

        /// A player's kill reaches the host first as a NpcDied request,
        /// which the host checks; a dead state that didn't come that way
        /// isn't taken. Nothing brings a dead NPC back, so an alive state
        /// over the host's dead one is out of date. In this order: an
        /// unreadable state is taken (Apply reports it); the host's NPC is
        /// dead; theirs is alive; a kill was allowed.
        public static NpcStateOutcome Judge(in NpcStateFacts npc)
        {
            if (!npc.Readable)
            {
                return NpcStateOutcome.Taken;
            }

            if (npc.OursKnown && npc.OursDead)
            {
                if (npc.TheirsDead)
                {
                    return NpcStateOutcome.Taken;
                }

                return NpcStateOutcome.Stale;
            }

            if (!npc.TheirsDead || npc.KillAllowed)
            {
                return NpcStateOutcome.Taken;
            }

            return NpcStateOutcome.NotAllowed;
        }

        /// Whether the dead NPC is known by its save id: above 0 and below
        /// the ids every game shares (SharedIds.SendLimit). Several copies
        /// of a story NPC can stand at once; then only that copy dies.
        public static bool ById(int deadId, int sendLimit)
        {
            return deadId > 0 && deadId < sendLimit;
        }

        /// Whether this NPC dies here: the same name, the same save id when
        /// the dead one is known by it, alive, and not the copy of an enemy
        /// the host runs.
        public static bool Kills(in NpcCopyFacts npc)
        {
            if (!npc.SameName)
            {
                return false;
            }

            if (npc.ById && (!npc.Saved || npc.SaveId != npc.DeadId))
            {
                return false;
            }

            return npc.Alive && !npc.Puppet;
        }
    }
}
