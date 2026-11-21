namespace DarkwoodMP.Sync.Players
{
    /// A light about to mark the pathfinding nodes it lights, and the
    /// nearest grid (LightGridPatch.Prefix).
    public readonly struct LightGridFacts
    {
        /// AstarPath.active isn't null.
        public bool Pathfinding { get; init; }

        /// getNearestGraphFast found a grid whose node array isn't null.
        public bool Graph { get; init; }

        /// The grid's node count.
        public int Nodes { get; init; }

        /// The grid's nodeSize.
        public float NodeSize { get; init; }

        /// The grid's Width and Depth, in nodes.
        public int Width { get; init; }

        public int Depth { get; init; }

        /// The light's position (transform.position).
        public Vec3 Light { get; init; }

        /// The first node's position (nodes[0].position as a Vector3).
        public Vec3 Origin { get; init; }

        /// The game's node light radius (_nodeLightRadius).
        public int Radius { get; init; }
    }
}
