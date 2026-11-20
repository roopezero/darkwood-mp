namespace DarkwoodMP.World.Rolls
{
    /// What a joiner does with a scripted event step (RollRules.PlayStep).
    public enum StepPlay : byte
    {
        /// Not playing the host's night event: the step runs as the game
        /// has it.
        Untouched = 0,

        /// Seen or heard: it runs, with Presentation and Replaying set
        /// around each of its steps (WorldEvents.AsPresentation).
        Presented,

        /// It changes the world: left to the host, the step does nothing.
        Dropped,
    }
}
