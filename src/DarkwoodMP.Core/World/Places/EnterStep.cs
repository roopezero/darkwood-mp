namespace DarkwoodMP.World.Places
{
    /// The host, keeping a location running for a joiner in it.
    public enum EnterStep : byte
    {
        /// Entered and active already.
        None = 0,

        /// Enter it quietly (its enter events are the joiner's).
        Enter,

        /// It is inactive: enter it quietly with force.
        ForceEnter,
    }
}
