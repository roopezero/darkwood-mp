namespace DarkwoodMP.Combat.Nights
{
    /// Host: a remote player's stand-in when the night's attackers are
    /// due, in NetPlayerProxy.All's order. Nulls are left out.
    public readonly struct NightPlayer
    {
        /// NetPlayerProxy.alive.
        public bool Alive { get; init; }

        /// In a separate location the host runs for them (InLocation).
        public bool Separate { get; init; }

        /// NightSpawns.BigLocationAt found a location at the stand-in.
        public bool AtPlace { get; init; }

        /// That location.
        public int Place { get; init; }

        /// That location is a hideout (Location.playerBase).
        public bool Base { get; init; }
    }
}
