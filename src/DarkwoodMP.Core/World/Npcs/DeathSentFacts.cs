namespace DarkwoodMP.World.Npcs
{
    /// What a game knows when one of its characters' Character.die
    /// returned (the plugin's NpcDiedPatch).
    public readonly struct DeathSentFacts
    {
        /// This is the outermost of the patched calls
        /// (WorldActions.Outermost(), called first).
        public bool Outermost { get; init; }

        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// The character has an NPC (Character.npc).
        public bool HasNpc { get; init; }

        /// Character.alive before the call.
        public bool WasAlive { get; init; }
    }
}
