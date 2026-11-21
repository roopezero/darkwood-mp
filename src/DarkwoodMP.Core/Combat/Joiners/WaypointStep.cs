namespace DarkwoodMP.Combat.Joiners
{
    /// What gotoNextWaypoint's prefix does with a noted chase.
    public enum WaypointStep
    {
        /// Nothing: no chase noted, or a waypoint was given.
        Leave,

        /// Forget the chase; the enemy checks out no location.
        Forget,

        /// Forget the chase and give the waypoint nearest the stand-in
        /// (JoinerAiRules.NearestWaypoint).
        Pick,
    }
}
