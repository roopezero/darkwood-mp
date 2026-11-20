namespace DarkwoodMP.Combat.Enemies
{
    /// Which loop a shown copy's idle sound starts with
    /// (CopyStateRules.IdleLoopFor).
    public enum IdleLoop
    {
        /// None: it's dead.
        None = 0,

        /// CharacterSounds.sleeping.
        Sleeping,

        /// CharacterSounds.idleLoop.
        Idle,
    }
}
