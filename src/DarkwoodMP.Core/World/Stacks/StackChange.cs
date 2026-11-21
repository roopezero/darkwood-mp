namespace DarkwoodMP.World.Stacks
{
    /// What a change of our own StackSize setting does.
    public enum StackChange
    {
        /// A client in a session keeps the host's size until it leaves.
        Ignore,

        /// Set our size on the item prefabs.
        Apply,

        /// Set it, and as the host send it to everyone.
        ApplyAndSend,
    }
}
