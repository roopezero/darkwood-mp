namespace DarkwoodMP.World.Hideout
{
    /// An oven lit, as OvenLitMessage carries it.
    public readonly struct LitOven
    {
        /// The oven lit.
        public ObjName Lit { get; init; }

        /// The oven that goes out; Kind None for none.
        public ObjName Out { get; init; }
    }
}
