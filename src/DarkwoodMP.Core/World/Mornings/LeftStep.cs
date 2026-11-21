namespace DarkwoodMP.World.Mornings
{
    /// What the host does with an AfterNightLeft. Every step but Ignore
    /// first remembers today's morning as over.
    public enum LeftStep : byte
    {
        /// Not the host, not a peer, not in a world, or no Controller.
        Ignore = 0,

        /// End our morning once our dialogue with the visitor is closed.
        EndAfterTalk,

        /// End our morning now (its end tells everyone).
        End,

        /// We have no morning of our own (or dream): send AfterNightEnded
        /// to everyone but the sender.
        Relay,
    }
}
