namespace DarkwoodMP.Combat.Joiners
{
    /// One of a location's waypointsInside, for JoinerAiRules.NearestWaypoint.
    public readonly struct WaypointSpot
    {
        /// The waypoint isn't null.
        public bool Exists { get; init; }

        /// Its squared 3D distance from the stand-in
        /// ((w - at).sqrMagnitude).
        public float SqrDistance { get; init; }
    }
}
