namespace DarkwoodMP.World.Talk
{
    /// What DialogueRules.Watch decided.
    public readonly struct DialogueTick
    {
        public DialogueAction Action { get; init; }

        /// The watch to keep, set before the action: ForceClose resets it.
        public DialogueWatch Watch { get; init; }
    }
}
