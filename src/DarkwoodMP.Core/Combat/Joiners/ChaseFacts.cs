namespace DarkwoodMP.Combat.Joiners
{
    /// An enemy about to check out a location (Character.checkOutLocation
    /// on the host), for JoinerChases.
    public readonly struct ChaseFacts
    {
        /// Threat is on (ThreatRules.IsOn).
        public bool On { get; init; }

        /// Character.checkingOutLocation: it is already at it.
        public bool CheckingOut { get; init; }

        /// Its superTarget is a stand-in.
        public bool StandIn { get; init; }

        /// That stand-in is alive.
        public bool StandInAlive { get; init; }

        /// JoinerAi.InsideAt the stand-in's position: on indoor ground.
        public bool StandInInside { get; init; }
    }
}
