namespace DarkwoodMP.Combat.Hostiles
{
    /// Host: what a client's NpcHit or NpcAttack names, as the host's
    /// world has it (HostileNpcs.Resolve).
    public readonly struct NpcAskFacts
    {
        /// The ObjRef names a character here (ObjRef.Resolve).
        public bool Found { get; init; }

        /// It is an NPC (Character.npc isn't null).
        public bool IsNpc { get; init; }

        /// It is in play here (NetEnemies.InPlay).
        public bool InPlay { get; init; }

        /// The requester has a stand-in (NetPlayerProxy.All).
        public bool HasStandIn { get; init; }
    }
}
