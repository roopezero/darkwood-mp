namespace DarkwoodMP.Combat.Enemies
{
    /// One copy or kept corpse of the host's enemy as a location is
    /// switched on or off here (the plugin's
    /// NetEnemies.LocationCharacters).
    public readonly struct CorpseFacts
    {
        /// Made here from its prefab (Puppet.Ours).
        public bool MadeHere { get; init; }

        /// Its character exists.
        public bool Exists { get; init; }

        /// Character.alive.
        public bool Alive { get; init; }

        /// Its GameObject's activeSelf is already what the location is
        /// switched to.
        public bool Already { get; init; }

        /// Core.withinLocation(its position, 0, the location).
        public bool Within { get; init; }
    }
}
