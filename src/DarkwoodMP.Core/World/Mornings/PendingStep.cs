namespace DarkwoodMP.World.Mornings
{
    /// What a waiting end does this frame.
    public enum PendingStep : byte
    {
        /// Nothing is waiting.
        Nothing = 0,

        /// No Controller, window, player or world: forget the end.
        Forget,

        /// The dialogue is closed: end the morning.
        End,

        /// Close the dialogue the game's way (DialogueGuard.Close), then
        /// ask MorningRules.AfterClose.
        Close,
    }
}
