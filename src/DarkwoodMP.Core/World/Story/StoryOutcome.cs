namespace DarkwoodMP.World.Story
{
    /// What the host does with a joiner's story step now.
    public enum StoryOutcome : byte
    {
        /// Run it in the host's game.
        Run = 0,

        /// It already ran: nothing to do.
        Done,

        /// The host can't run it just now: hold it.
        Wait,

        /// It doesn't fit the story.
        Refuse,
    }
}
