namespace DarkwoodMP.World.Inventories
{
    /// What the host knows about a client and a container it changed.
    public readonly struct ContainerFacts
    {
        /// The client has a live stand-in (NetPlayerProxy.All).
        public bool Known { get; init; }

        /// Where the stand-in is, on the ground plane.
        public Vec2 StandIn { get; init; }

        /// Where the container is, on the ground plane.
        public Vec2 At { get; init; }

        /// Whether it's shut in the host's world.
        public LockFacts Lock { get; init; }
    }
}
