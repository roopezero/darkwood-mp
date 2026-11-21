namespace DarkwoodMP.Combat.Nights
{
    /// An occupied hideout and whom its attacker is aimed at: the host's
    /// player, or the stand-in at Proxy in the list given.
    public readonly struct NightTarget
    {
        public int Place { get; init; }

        /// Aimed at the host's player.
        public bool Host { get; init; }

        /// The stand-in's index, when not Host.
        public int Proxy { get; init; }
    }
}
