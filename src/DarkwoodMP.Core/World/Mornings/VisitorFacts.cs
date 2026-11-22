namespace DarkwoodMP.World.Mornings
{
    /// Whether our dialogue is with a hideout's morning visitor.
    public readonly struct VisitorFacts
    {
        /// Player.Instance exists.
        public bool HasPlayer { get; init; }

        /// The UI's dialogueWindow exists.
        public bool HasWindow { get; init; }

        /// The window has an NPC.
        public bool HasNpc { get; init; }

        /// Player.inDialogue.
        public bool InDialogue { get; init; }

        /// WorldGenerator and its locations exist.
        public bool HasLocations { get; init; }

        /// The NPC is a playerBase location's trader or wolf, or is under
        /// one of them (Transform.IsChildOf).
        public bool AtBase { get; init; }
    }
}
