namespace DarkwoodMP.World.Npcs
{
    /// Whether an NPC's position may be shared and set (NpcSync.Movable).
    public readonly struct NpcFacts
    {
        /// The NPC has a Character.
        public bool HasCharacter { get; init; }

        /// Character.alive.
        public bool Alive { get; init; }

        /// NetEnemies runs it: a session (CoopSession.Enemies) exists and has
        /// a net id for the Character.
        public bool RunByEnemies { get; init; }
    }
}
