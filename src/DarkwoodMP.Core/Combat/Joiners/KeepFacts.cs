namespace DarkwoodMP.Combat.Joiners
{
    /// One entry of the stand-in registry (NetPlayerProxy.All), when the
    /// host checks whether to keep it (Proxies.Update).
    public readonly struct KeepFacts
    {
        /// The stand-in object isn't destroyed.
        public bool Exists { get; init; }

        /// Stand-ins are wanted at all (ProxyRules.Wanted).
        public bool Wanted { get; init; }

        /// Its player is still among the remote players
        /// (PlayerSync.Remotes).
        public bool Known { get; init; }

        /// This game hosts.
        public bool Hosting { get; init; }

        /// It is the test dummy's (PlayerSync.DummyId).
        public bool Dummy { get; init; }
    }
}
