namespace DarkwoodMP.Sync.Players
{
    /// Whether a light is so far outside the pathfinding grid that the
    /// game's node lookup would read past the grid's node array
    /// (LightGridPatch). It runs with or without a session: a shared
    /// dream's level stays in the host's world with its lamps far off the
    /// grid.
    public static class LightGridRules
    {
        /// The game's lookup is skipped (and the light's nodes cleared)
        /// only for a light outside the grid by more than the node light
        /// radius. Without pathfinding, a grid, nodes or a positive node
        /// size, the game's method runs and decides itself.
        public static bool SkipsLookup(in LightGridFacts light)
        {
            if (!light.Pathfinding || !light.Graph || light.Nodes == 0)
            {
                return false;
            }

            if (light.NodeSize <= 0f)
            {
                return false;
            }

            return !Inside(light);
        }

        /// The light's node, rounded (halves to even) from the first node,
        /// is within the radius of the grid's edge.
        static bool Inside(in LightGridFacts light)
        {
            float dx = light.Light.X - light.Origin.X;
            float dz = light.Light.Z - light.Origin.Z;
            int x = GameMath.RoundToInt(dx / light.NodeSize);
            int z = GameMath.RoundToInt(dz / light.NodeSize);
            int r = light.Radius;
            if (x < -r || x >= light.Width + r)
            {
                return false;
            }

            return z >= -r && z < light.Depth + r;
        }
    }
}
