namespace DarkwoodMP.World.Fuel
{
    /// What the host knows about the generator or saw a client's Fuel
    /// names, and the amount it poured (above 0) or cut (0 and below).
    public readonly struct FuelFacts
    {
        /// A Generator (Tank is Generator) or a Saw (any other value) was
        /// found by its ObjRef.
        public bool Found { get; init; }

        /// FuelRules.TankOf the message's what byte.
        public FuelTank Tank { get; init; }

        /// The ObjRef the message names, for the "not here" refusals.
        public ObjName Ref { get; init; }

        /// The amount the client added: poured above 0, cut at 0 and below.
        public float Value { get; init; }

        /// The fuel it has in the host's world (fuel).
        public float Have { get; init; }

        /// The most it holds (maxFuel).
        public float Max { get; init; }

        /// How much a can holds (FuelRules.CanHolds).
        public float CanHolds { get; init; }

        /// The fuel one log takes (Saw.fuelToConvert; saws only).
        public int FuelToConvert { get; init; }

        /// The requester's stand-in and the tank, for Authority.Near. The
        /// rule sets the reach.
        public ReachFacts Reach { get; init; }
    }
}
