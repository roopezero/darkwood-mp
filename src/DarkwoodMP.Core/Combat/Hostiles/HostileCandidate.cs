namespace DarkwoodMP.Combat.Hostiles
{
    /// Client: one of our characters, as a copy of the host's NPC of a
    /// kind (HostileNpcs.OurCopy). Nulls aren't candidates.
    public readonly struct HostileCandidate
    {
        public Vec3 Pos { get; init; }

        /// It is an NPC (Character.npc isn't null).
        public bool IsNpc { get; init; }

        /// It is in HostileNpcs' dead set: an NPC that died here.
        public bool DiedHere { get; init; }

        /// It already shows one of the host's enemies.
        public bool Driven { get; init; }

        /// Its kind (NetEnemies.KindOf).
        public string Kind { get; init; }
    }
}
