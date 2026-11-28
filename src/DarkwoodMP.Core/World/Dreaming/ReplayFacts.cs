namespace DarkwoodMP.World.Dreaming
{
    /// What our player has open as another game's end of the shared dream
    /// is replayed here (DreamSync.Replay).
    public readonly struct ReplayFacts
    {
        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// Until when a dialogue that is opening or closing is waited for
        /// (DreamRules.ReplayBy).
        public Seconds WaitBy { get; init; }

        /// The dialogue window is on screen or on its way (Player.inDialogue,
        /// or the window switched on).
        public bool DialogueOpen { get; init; }

        /// The dialogue window is opening or closing (DialogueWindow.tweening,
        /// or switched on and not shown yet).
        public bool DialogueTweening { get; init; }

        /// The map or the journal is open.
        public bool MenuOpen { get; init; }
    }
}
