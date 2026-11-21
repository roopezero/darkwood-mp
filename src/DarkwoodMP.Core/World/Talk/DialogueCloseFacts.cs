namespace DarkwoodMP.World.Talk
{
    /// What closing a dialogue the game's way looks at
    /// (DialogueGuard.Close).
    public readonly struct DialogueCloseFacts
    {
        /// There is a window.
        public bool HasWindow { get; init; }

        /// DialogueWindow.tweening.
        public bool Tweening { get; init; }

        /// Its npc field is set and its object destroyed.
        public bool Destroyed { get; init; }

        /// Its npc is Unity's null (destroyed, or cleared).
        public bool Cleared { get; init; }

        /// A Player, and Player.inShop.
        public bool InShop { get; init; }
    }
}
