namespace DarkwoodMP.World.Npcs
{
    /// What an NpcDied names: the thing its ObjRef finds, the NPC's name and
    /// where it stood (NpcDeaths.Dying's arguments).
    public readonly struct DyingFacts
    {
        /// The ObjRef finds a Character with an NPC.
        public bool RefIsNpc { get; init; }

        /// That NPC's name (Character.npc.name), when RefIsNpc.
        public string RefName { get; init; }

        /// The name the action carries (its s); may be empty or null.
        public string Name { get; init; }

        /// Where it stood (the action's x and y, the game's x and z).
        public Vec2 At { get; init; }
    }
}
