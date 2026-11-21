namespace DarkwoodMP.World.Rolls
{
    /// When the fog fades in or out (the game's TimeAndDay), or none.
    public readonly struct FogTime
    {
        /// There is a time (the TimeAndDay isn't null).
        public bool Set { get; init; }

        /// TimeAndDay.day.
        public int Day { get; init; }

        /// TimeAndDay.time.
        public int Time { get; init; }
    }
}
