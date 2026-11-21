namespace DarkwoodMP.World.Dreaming
{
    /// What DreamSync.PresetChosen does with a preset the game drew.
    public enum PresetStep
    {
        /// Not our level dream, or the game is loading: nothing, and the
        /// mark stays for the next draw.
        Keep,

        /// The mark is cleared and nothing is sent.
        Clear,

        /// The mark is cleared, the dream is shared and sent to everyone.
        Share,
    }
}
