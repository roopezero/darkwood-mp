namespace DarkwoodMP.World.Breaking
{
    /// The world's call guards: WorldActions.Depth and Replaying.
    public readonly struct Guards
    {
        public int Depth { get; init; }

        public bool Replaying { get; init; }
    }
}
