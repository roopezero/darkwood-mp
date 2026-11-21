namespace DarkwoodMP.World.Mornings
{
    /// A remote player as the host's late-morning check sees them.
    public readonly struct RemoteMorning
    {
        /// Their RemotePlayer has a last PlayerState.
        public bool HasState { get; init; }

        /// That state says they are in a morning (its AfterNight).
        public bool InMorning { get; init; }

        /// They are one of Session.Peers.
        public bool IsPeer { get; init; }
    }
}
