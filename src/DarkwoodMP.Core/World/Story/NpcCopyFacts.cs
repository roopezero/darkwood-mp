namespace DarkwoodMP.World.Story
{
    /// One NPC in this game, when another game's state says an NPC of
    /// that name died (NpcStateRules.Kills).
    public readonly struct NpcCopyFacts
    {
        /// Its name is the dead NPC's (NPC.name).
        public bool SameName { get; init; }

        /// The dead one is known by its save id (NpcStateRules.ById).
        public bool ById { get; init; }

        /// It has a SaveableObject.
        public bool Saved { get; init; }

        /// Its save id (SaveableObject.uniqueId), when Saved.
        public int SaveId { get; init; }

        /// The save id of the one that died (NPCState.deadID).
        public int DeadId { get; init; }

        /// It has a Character, and it is alive (Character.alive).
        public bool Alive { get; init; }

        /// It is a copy of an enemy the host runs (NetEnemies.IsPuppet):
        /// it dies with the host's state instead.
        public bool Puppet { get; init; }
    }
}
