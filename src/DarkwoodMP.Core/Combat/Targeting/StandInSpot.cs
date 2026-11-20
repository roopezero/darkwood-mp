namespace DarkwoodMP.Combat.Targeting
{
    /// Where a remote player's stand-in (NetPlayerProxy) is, for the
    /// nearest-player rules. The shell leaves out a null one.
    public readonly struct StandInSpot
    {
        /// CharBase.alive: false while its player is dead or away.
        public bool Alive { get; init; }

        /// Its position.
        public Vec3 At { get; init; }

        /// From the enemy or thing asked about, as the game measures it
        /// (Core.trueDistance).
        public float Distance { get; init; }
    }
}
