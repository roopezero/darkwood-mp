namespace DarkwoodMP.World.Fuel
{
    /// What MsgType.Fuel carries: the generator or saw, and its fuel.
    public readonly struct TankFuel
    {
        /// The generator's or saw's ObjRef.
        public ObjName Tank { get; init; }

        /// What it is (FuelRules.TankOf the what byte).
        public FuelTank Kind { get; init; }

        /// From a client: the amount its player added (a pour above 0, a
        /// cut at 0 and below). From the host: the level.
        public float Value { get; init; }

        /// From the host, generators: it runs. A client sends false.
        public bool On { get; init; }
    }
}
