namespace DarkwoodMP.World.Talk
{
    /// Each frame: the open dialogue and its NPC (DialogueGuard.Update).
    public readonly struct DialogueFacts
    {
        /// A Player and a dialogue window, and Player.inDialogue.
        public bool Open { get; init; }

        /// The window's npc is Unity's null: destroyed, or cleared by a
        /// close on its way.
        public bool Gone { get; init; }

        /// The window's npc field is set and its object destroyed.
        public bool Destroyed { get; init; }

        /// When not Gone: the NPC's gameObject.activeInHierarchy.
        public bool Active { get; init; }

        /// Hideouts.Together.
        public bool Together { get; init; }

        /// DialogueWindow.tweening: opening or closing.
        public bool Tweening { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }
    }
}
