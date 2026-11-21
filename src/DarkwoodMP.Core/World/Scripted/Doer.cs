namespace DarkwoodMP.World.Scripted
{
    /// Whether a trigger set off now is our own player's doing.
    public enum Doer : byte
    {
        /// Not ours.
        No = 0,

        /// Ours.
        Yes,

        /// Ours if our player's call is on the stack
        /// (WorldActions.ByLocalPlayer) or a dialogue window's is
        /// (ScriptedEvents.OnStack): the shell reads the stack.
        Stack,
    }
}
