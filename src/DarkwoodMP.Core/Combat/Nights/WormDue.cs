namespace DarkwoodMP.Combat.Nights
{
    /// Who gets a night worm at a tick of the worm loop.
    public enum WormDue
    {
        /// Nobody.
        None,

        /// Each stand-in NightRules.WormWanted picks, not the host.
        Proxies,

        /// The host's player first, then each stand-in WormWanted picks.
        HostAndProxies,
    }
}
