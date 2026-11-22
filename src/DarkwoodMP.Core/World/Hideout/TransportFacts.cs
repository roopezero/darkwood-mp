namespace DarkwoodMP.World.Hideout
{
    /// Location.transportAllItemsToCurrentHideout is about to run.
    public readonly struct TransportFacts
    {
        /// Playing together (TogetherRules.Together).
        public bool Together { get; init; }

        /// This game is a client (Session.IsClient).
        public bool IsClient { get; init; }

        /// The running scripted event part is another player's
        /// (EventParts.Current is Part.SharedOnly).
        public bool OthersPart { get; init; }

        /// The running part is a joiner's on the host
        /// (EventParts.Current is Part.HostAndShared).
        public bool JoinersPart { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }
    }
}
