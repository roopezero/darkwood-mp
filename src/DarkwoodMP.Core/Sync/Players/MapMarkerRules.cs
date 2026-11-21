namespace DarkwoodMP.Sync.Players
{
    /// Which other players are marked on the game's world map, and where
    /// (MapMarkers). Markers are made when the map opens; while it's open
    /// they're only hidden, shown and moved.
    public static class MapMarkerRules
    {
        /// The map type the markers go on.
        public const string WorldType = "World";

        /// How far above the map's background a marker is raised.
        public const float Raise = 15f;

        /// The map is open on the world type (Map.opened, currentType's
        /// name); null for no type.
        public static bool OnWorldMap(bool opened, string type) =>
            opened && type == WorldType;

        /// A puppet is on the map with a root, not away and not a
        /// spectator.
        public static bool OnMap(in MarkerFacts puppet)
        {
            if (!puppet.HasRoot || puppet.Away)
            {
                return false;
            }

            return !puppet.Spectating;
        }

        /// A marker is made at opening for each puppet on the map, except
        /// the dummy.
        public static bool Marks(in MarkerFacts puppet) =>
            !puppet.Dummy && OnMap(puppet);

        /// The marker's local position under the map's icons, as Map.open
        /// places an icon: the world position over the type's scale,
        /// raised by Raise (adding 0 to x and z, as the Vector3 sum does).
        /// The shell then moves it by IconShift in world space.
        public static Vec3 LocalSpot(Vec3 world, float scale) =>
            new Vec3(
                world.X / scale + 0f,
                world.Y / scale + Raise,
                world.Z / scale + 0f);

        /// The type's icon offset on screen, scaled to the resolution
        /// (Core.ResolutionWidthModifier and ResolutionHeightModifier as X
        /// and Z), added to the marker's world position.
        public static Vec3 IconShift(Vec2 offset, Vec2 resolution) =>
            new Vec3(
                offset.X * resolution.X,
                0f,
                offset.Z * resolution.Z);
    }
}
