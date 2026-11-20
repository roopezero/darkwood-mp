namespace DarkwoodMP.World.Talk
{
    /// What the frame watch does with the open dialogue.
    public enum DialogueAction
    {
        None,

        /// ForceClose(w, "destroyed").
        ForceDestroyed,

        /// ForceClose(w, "gone for 4 s").
        ForceGone,

        /// Close it the game's way (DialogueGuard.Close).
        Close,
    }
}
