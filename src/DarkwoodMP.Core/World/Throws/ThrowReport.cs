namespace DarkwoodMP.World.Throws
{
    /// What an ItemThrown says: who threw or dropped what, from where,
    /// and how it flies.
    public readonly struct ThrowReport
    {
        /// The thrower, as the sender names them.
        public ulong By { get; init; }
        /// The item's type (InvItem.type).
        public string Type { get; init; }
        /// Where the thrown thing is as it leaves.
        public Vec3 Pos { get; init; }
        /// Its rotation then.
        public Quat Rot { get; init; }
        /// Its velocity, where it lands and the distance there.
        public ThrowShot Shot { get; init; }
    }
}
