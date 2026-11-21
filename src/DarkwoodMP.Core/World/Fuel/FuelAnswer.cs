namespace DarkwoodMP.World.Fuel
{
    /// The host's answer to a client's Fuel.
    public readonly struct FuelAnswer
    {
        public Verdict Verdict { get; init; }

        /// Send the requester the level to roll back to, before the
        /// verdict. Every refusal does, but "not here".
        public bool SendsBack { get; init; }

        /// Allowed: the amount it took (the request's Result, through
        /// FuelRules.ResultOf).
        public float Took { get; init; }

        /// Allowed: the tank's new level, which then goes to everyone.
        public float Level { get; init; }
    }
}
