namespace DarkwoodMP.World.Mornings
{
    /// What a client does with an AfterNightEnded.
    public enum EndedStep : byte
    {
        /// Not a client, not from the host, not in a world, or pending.
        Ignore = 0,

        /// Our morning was over already (or we dream): a log line only.
        AlreadyOver,

        /// End our morning once our dialogue with the visitor is closed.
        EndAfterTalk,

        /// End our morning now, as a replay.
        End,
    }
}
