namespace DarkwoodMP.World.Mornings
{
    /// What a waiting end does after it tried to close the dialogue.
    public enum CloseStep : byte
    {
        /// Try again next frame.
        Wait = 0,

        /// The NPC let go: end the morning.
        End,

        /// It didn't close in time: a warning, DialogueGuard.ForceClose
        /// ("not closing"), then end the morning.
        ForceEnd,
    }
}
