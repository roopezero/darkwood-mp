namespace DarkwoodMP.World.Story
{
    /// One GameEvent step. The zero value stands for no step (null, or an
    /// index past the end of its event's list).
    public readonly struct StoryStepFacts
    {
        /// The step's type (GameEvent.type).
        public StepAction Action { get; init; }

        /// GameEvent.activeModifier.
        public bool ActiveModifier { get; init; }

        /// GameEvent.Value: the dream's name for startDream.
        public string Value { get; init; }

        /// GameEvent.intValue: the chapter for a chapter's step.
        public int Chapter { get; init; }
    }
}
