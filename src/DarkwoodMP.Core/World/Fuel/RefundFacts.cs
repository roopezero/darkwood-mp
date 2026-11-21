namespace DarkwoodMP.World.Fuel
{
    /// What a client knows when the host answers its Fuel: the answer, and
    /// the pour it asked about (FuelSync.Pour).
    public readonly struct RefundFacts
    {
        /// The host allowed it.
        public bool Allowed { get; init; }

        /// The fuel it asked to add.
        public float Amount { get; init; }

        /// How many pours came from a gasoline can.
        public float Drained { get; init; }

        /// The host's result: the amount it took, in the invariant culture.
        public string Result { get; init; }
    }
}
