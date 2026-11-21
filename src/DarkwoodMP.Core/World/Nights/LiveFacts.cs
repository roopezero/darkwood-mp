namespace DarkwoodMP.World.Nights
{
    /// A recorded night object, at the host's check each second.
    public readonly struct LiveFacts
    {
        /// The object still exists.
        public bool Alive { get; init; }

        /// The host shares the world (NightObjectRules.Sends).
        public bool Sending { get; init; }

        /// Its trap's state when last sent (NightObjectRules.NoTrap,
        /// Armed or Unarmed).
        public byte Sent { get; init; }

        /// Its trap's state now.
        public byte Now { get; init; }
    }
}
