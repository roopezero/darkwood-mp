namespace DarkwoodMP.World.Story
{
    /// An EpilogueStep message as read: its step and its value.
    public readonly struct EpilogueNote
    {
        /// The step (EpilogueStep, as the byte was sent).
        public EpilogueStep Step { get; init; }

        /// The value: the world event, "1" or "0", "" or the GameEvents.
        public string Value { get; init; }
    }
}
