namespace DarkwoodMP.World.Npcs
{
    /// One NPC in the host's world (FindObjectsOfType<NPC>) that an NpcDied
    /// may name.
    public readonly struct NpcCandidate
    {
        /// The NPC's GameObject name (NPC.name).
        public string Name { get; init; }

        /// It has a Character.
        public bool HasCharacter { get; init; }

        /// Character.alive.
        public bool Alive { get; init; }

        /// Where its Character is, on the ground plane.
        public Vec2 At { get; init; }
    }
}
