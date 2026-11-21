namespace DarkwoodMP.Combat.Joiners
{
    /// Where a stand-in's player is, for the host's enemies.
    public readonly struct Presence
    {
        /// Not in the host's world: in a dream of their own, or a location
        /// the host hasn't got. Their own game runs that area.
        public bool Away { get; init; }

        /// In a location the host runs for them (NetPlayerProxy.InLocation
        /// is set).
        public bool InLocation { get; init; }

        /// Alive and not away: the stand-in is alive and its collider on.
        public bool Here { get; init; }
    }
}
