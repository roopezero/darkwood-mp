namespace DarkwoodMP.World.Throws
{
    /// What ThrowSync.OnThrown looks at when an ItemThrown comes.
    public readonly struct ThrownFacts
    {
        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        public bool IsHost { get; init; }

        public bool IsClient { get; init; }

        /// Who sent the message.
        public ulong From { get; init; }

        /// Session.HostId.
        public ulong HostId { get; init; }

        /// Session.LocalId: this game's player.
        public ulong LocalId { get; init; }

        /// The thrower the message names.
        public ulong ReportedBy { get; init; }

        /// The sender has a live stand-in here (NetPlayerProxy.All).
        public bool Known { get; init; }

        /// Where it is (x, z).
        public Vec2 StandIn { get; init; }

        /// Where the thing left from (x, z).
        public Vec2 Pos { get; init; }
    }
}
