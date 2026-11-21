namespace DarkwoodMP.World.Nights
{
    /// A pending second arming, each frame.
    public readonly struct RearmFacts
    {
        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// When it is due.
        public Seconds At { get; init; }

        /// The object still exists and has a Trigger.
        public bool HasTrap { get; init; }

        /// The Trigger is active.
        public bool Active { get; init; }

        /// The host's state for it (NightObjectRules.Armed or Unarmed).
        public byte Armed { get; init; }
    }
}
