namespace DarkwoodMP.World.Traders
{
    /// One NPC here that a message about a trader might mean.
    public readonly struct TraderCandidate
    {
        /// NPC.trader.
        public bool Trader { get; init; }

        /// The NPC's GameObject name.
        public string Name { get; init; }

        /// He has an inventory, set or as his component (TraderSync.Inv).
        public bool HasInventory { get; init; }

        /// His own ref is from the shared save (ObjRef.Kind.Saved).
        public bool Saved { get; init; }

        /// Where he stands (x, z).
        public Vec2 Position { get; init; }
    }
}
