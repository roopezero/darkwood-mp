namespace DarkwoodMP.World.Story
{
    /// Where a new game starts (the Start Game window's "Start at").
    public enum StoryStart : byte
    {
        /// The game's prologue, its two dream stages.
        Prologue = 0,

        /// Chapter 1 at the hideout: the prologue skipped.
        ChapterOne = 1,

        /// Chapter 1 with the prologue skipped, and the host's "Start the
        /// epilogue" button in the Multiplayer window: for trying the ending
        /// together without playing the whole game.
        Epilogue = 2,
    }
}
