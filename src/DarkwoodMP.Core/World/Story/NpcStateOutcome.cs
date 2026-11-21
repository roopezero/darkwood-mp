namespace DarkwoodMP.World.Story
{
    /// What the host does with a joiner's NPC state (NpcStateRules.Judge).
    public enum NpcStateOutcome : byte
    {
        /// Applied and relayed.
        Taken = 0,

        /// Alive over the host's dead NPC: sent before the death reached
        /// them. Not taken; the host's state goes back to the sender, and
        /// nothing is logged.
        Stale,

        /// Dead, but no kill of theirs was allowed here. Not taken; logged
        /// as refused, and the host's state, if it has one, goes back.
        NotAllowed,
    }
}
