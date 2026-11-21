namespace DarkwoodMP.World.Scripted
{
    /// A trigger's coroutine just started (EventTrigger.fire), for what to
    /// remember about it.
    public readonly struct RememberFacts
    {
        /// This game is the host and has peers.
        public bool HostWithPeers { get; init; }

        /// The host's world runs it for everyone
        /// (WorldTriggerRules.HostsWorldTrigger).
        public bool HostsWorld { get; init; }

        /// Our own player is setting it off (ScriptedEvents.SettingOff).
        public bool SettingOff { get; init; }

        /// The EventTriggers it belongs to exists.
        public bool HasTriggers { get; init; }

        /// The trigger's own type is onInSightOfPlayer or
        /// onOutOfSightOfPlayer, and which.
        public bool Sight { get; init; }

        public bool OutOfSight { get; init; }

        /// EventTrigger.multipleFire.
        public bool MultipleFire { get; init; }

        /// The part being run now (EventParts.Current).
        public EventPart Current { get; init; }

        /// The type set off is a shared replay here
        /// (DoerRules.SharedReplay).
        public bool SharedReplay { get; init; }
    }
}
