namespace DarkwoodMP.World.Npcs
{
    /// What the host knows about an NPC a client says died (NpcDied).
    public readonly struct NpcDeathFacts
    {
        /// The NPC the death names was found (NpcDeathRules.Pick).
        public bool Found { get; init; }

        /// The requester and that NPC. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
