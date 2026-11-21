namespace DarkwoodMP.World.Story
{
    /// What a joiner's StoryRequest asks for: the story step its event
    /// reached.
    public readonly struct StoryAsk
    {
        /// The GameEvents (the zero name when the step has no event).
        public ObjName Event { get; init; }

        /// The step's index in its event's list (0 with no event).
        public byte Index { get; init; }

        public StoryKind Kind { get; init; }

        /// The chapter (0 unless Kind is Chapter).
        public int Chapter { get; init; }
    }
}
