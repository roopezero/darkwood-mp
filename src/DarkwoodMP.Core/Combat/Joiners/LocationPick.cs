namespace DarkwoodMP.Combat.Joiners
{
    /// Whose location a spawned character's activity uses.
    public enum LocationPick
    {
        /// The host's player's big location (null-safe).
        Host,

        /// The location the character was sent at (NightSpawns.SentAt).
        Sent,

        /// The big location at the chosen stand-in's position.
        StandIn,
    }
}
