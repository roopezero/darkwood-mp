namespace DarkwoodMP.Combat.Joiners
{
    /// Which player an activity aimed at "the player" goes for.
    public enum PlayerPick
    {
        /// The host's player.
        Host,

        /// The stand-in the character was sent at.
        Sent,

        /// "The player" by threat (Threat.BestPlayerFor).
        Best,
    }
}
