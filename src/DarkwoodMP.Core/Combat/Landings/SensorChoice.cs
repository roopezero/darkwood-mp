namespace DarkwoodMP.Combat.Landings
{
    /// One of our copy's Character.sensorTypes, for EnemySensorThrown.
    /// A null entry is one that isn't Ranged.
    public readonly struct SensorChoice
    {
        /// SensorType.isRanged.
        public bool Ranged { get; init; }

        /// SensorType.name.
        public string Name { get; init; }

        /// SensorType.sensor isn't null.
        public bool HasSensor { get; init; }
    }
}
