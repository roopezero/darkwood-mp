namespace DarkwoodMP.Combat.Enemies
{
    /// Whether a copy's animator is paused or resumed to match the host's
    /// (CopyStateRules.Play).
    public enum PlayStep
    {
        /// Nothing.
        None = 0,

        /// Animator.Pause.
        Pause,

        /// Animator.Resume.
        Resume,
    }
}
