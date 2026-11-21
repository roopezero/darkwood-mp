namespace DarkwoodMP.World.Story
{
    /// What a game does with another player's journal entry
    /// (JournalRules.Take).
    public enum JournalTake : byte
    {
        /// Added, if the journal hasn't got it yet.
        Add = 0,

        /// The value is StoryKeys.Removed: taken out of the journal (only
        /// entries, notes, keys and items can be).
        Remove,

        /// Found in a dream this game isn't in (or one that has ended
        /// here): not taken, since the game would never drop it.
        OtherDream,

        /// A kind the journal has no part for: nothing happens.
        Unknown,
    }
}
