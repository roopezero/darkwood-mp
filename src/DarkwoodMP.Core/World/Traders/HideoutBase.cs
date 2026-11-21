namespace DarkwoodMP.World.Traders
{
    /// A Location the host might put a morning trader in.
    public readonly struct HideoutBase
    {
        /// Location.playerBase.
        public bool PlayerBase { get; init; }

        /// It has a Location.traderPosition.
        public bool HasTraderSpot { get; init; }

        /// Where that is (x, z).
        public Vec2 TraderSpot { get; init; }
    }
}
