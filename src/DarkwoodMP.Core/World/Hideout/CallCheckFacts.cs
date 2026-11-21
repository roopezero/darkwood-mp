namespace DarkwoodMP.World.Hideout
{
    /// The host, checking a joiner's porter call (PorterCalled).
    public readonly struct CallCheckFacts
    {
        /// A location's porter place is near the position
        /// (PorterRules.NearestPorterPlace found one).
        public bool HasPlace { get; init; }

        /// The requester has a stand-in (Authority.Requester).
        public bool Known { get; init; }

        /// The big location at the stand-in is that location
        /// (NightSpawns.BigLocationAt).
        public bool AtPlace { get; init; }

        /// That location's name, for the refusal's words.
        public string PlaceName { get; init; }

        /// Core.isDay().
        public bool IsDay { get; init; }

        /// The flag PorterRules.InTransit is true.
        public bool InTransit { get; init; }

        /// The flag PorterRules.Killed is true.
        public bool Killed { get; init; }

        /// The location has its porter (Location.porter).
        public bool PorterThere { get; init; }
    }
}
