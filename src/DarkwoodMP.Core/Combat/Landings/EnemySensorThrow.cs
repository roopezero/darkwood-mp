namespace DarkwoodMP.Combat.Landings
{
    /// A ranged sensor's prefab one of the host's enemies threw
    /// (EnemySensorThrownMessage).
    public readonly struct EnemySensorThrow
    {
        public EnemyThrow Throw { get; init; }

        /// Where it lands on the ground (ThrownItem.landTarget's x and z).
        public Vec2 Land { get; init; }
    }
}
