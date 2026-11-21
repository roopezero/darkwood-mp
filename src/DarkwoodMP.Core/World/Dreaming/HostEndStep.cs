namespace DarkwoodMP.World.Dreaming
{
    /// What the host does with a DreamEnd from a client.
    public enum HostEndStep
    {
        /// Dropped without a word.
        Drop,

        /// Another dream ended: logged as not passed on.
        NotPassedOn,

        /// Sent on to everyone but the sender, then ours ends.
        RelayAndEnd,
    }
}
