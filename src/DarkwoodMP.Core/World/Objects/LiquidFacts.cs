namespace DarkwoodMP.World.Objects
{
    /// What the host knows about spilt gasoline a client's player lit
    /// (LiquidLit).
    public readonly struct LiquidFacts
    {
        /// It is in the host's world (ObjRef.Resolve<Transform>).
        public bool Exists { get; init; }

        /// It has a Liquid.
        public bool IsLiquid { get; init; }

        /// Liquid.flammable.
        public bool Flammable { get; init; }

        /// The requester and the liquid. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
