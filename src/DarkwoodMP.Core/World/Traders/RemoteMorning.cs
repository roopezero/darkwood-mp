namespace DarkwoodMP.World.Traders
{
    /// One remote player, for whether a joiner is in a morning
    /// (PlayerSync.Remotes).
    public readonly struct RemoteMorning
    {
        /// The test dummy (PlayerSync.DummyId), who doesn't count.
        public bool Dummy { get; init; }

        /// A PlayerState came from them (RemotePlayer.Last).
        public bool HasState { get; init; }

        /// That state's AfterNight.
        public bool AfterNight { get; init; }
    }
}
