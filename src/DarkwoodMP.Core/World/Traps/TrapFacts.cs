namespace DarkwoodMP.World.Traps
{
    /// What the host knows about a trap (a Trigger) when a client says it
    /// set it off, switched it or disarmed it.
    public readonly struct TrapFacts
    {
        /// The trap is in the host's world (ObjRef.Resolve<Trigger>).
        public bool Exists { get; init; }

        /// Trigger.active: it hasn't gone off.
        public bool Active { get; init; }

        /// Trigger.canDisarm.
        public bool CanDisarm { get; init; }

        /// The map test's trap-refuse, when it is on.
        public TrapTest Test { get; init; }

        /// The requester and the trap. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
