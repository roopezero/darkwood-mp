using System.Collections.Generic;

namespace DarkwoodMP.World.Fuel
{
    /// Who is near a generator that burns fuel on the host.
    public readonly struct NearestFacts
    {
        /// Where the generator is.
        public Vec2 Generator { get; init; }

        /// Where the host's player is.
        public Vec2 Host { get; init; }

        /// The host's player is away from its own world
        /// (SharedCullingPatch.HostAway): it is never the nearest.
        public bool HostAway { get; init; }

        /// The other players' stand-ins, in NetPlayerProxy.All's order.
        public IReadOnlyList<DrainPlayer> Players { get; init; }
    }
}
