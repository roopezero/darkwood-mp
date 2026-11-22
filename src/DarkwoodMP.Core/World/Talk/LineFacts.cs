namespace DarkwoodMP.World.Talk
{
    /// A dialogue window as a written line finishes
    /// (DialogueWindow.finishedElement).
    public readonly struct LineFacts
    {
        /// DialogueWindow.currentBoardElements.Count.
        public int Elements { get; init; }

        /// DialogueWindow.currentDialogue is set.
        public bool HasDialogue { get; init; }

        /// DialogueWindow.npc is there (Unity's null check).
        public bool HasNpc { get; init; }

        /// The window's gameObject.activeSelf.
        public bool Active { get; init; }
    }
}
