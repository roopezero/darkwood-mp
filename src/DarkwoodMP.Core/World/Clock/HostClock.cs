namespace DarkwoodMP.World.Clock
{
    /// The host's day and minute of the day, as a TimeSync carries them.
    public readonly struct HostClock
    {
        public int Day { get; init; }

        public int Minute { get; init; }
    }
}
