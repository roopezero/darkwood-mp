namespace DarkwoodMP.World.Npcs
{
    /// Client, each frame: one NPC the host placed, for
    /// NpcPlaceRules.Follow.
    public readonly struct NpcFollowFacts
    {
        /// SaveManager.uniqueIdDict has a live object for its save id.
        public bool HasObject { get; init; }

        /// NpcPlaceRules.Movable of the object's NPC (false with none).
        public bool Movable { get; init; }

        /// Where our copy is: its Character's position.
        public Vec3 At { get; init; }

        /// Where the host has it, x and z.
        public Vec2 Target { get; init; }

        /// Time.deltaTime: scaled, so copies don't slide while paused.
        public Seconds Delta { get; init; }
    }
}
