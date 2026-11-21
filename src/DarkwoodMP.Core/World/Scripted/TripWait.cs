namespace DarkwoodMP.World.Scripted
{
    /// A trip the movement check is to expect: which step, and for how
    /// long from now (PlayerSync.ExpectTrip).
    public readonly struct TripWait
    {
        public TripWait(int step, Seconds wait)
        {
            Step = step;
            Wait = wait;
        }

        /// The step's index in the events.
        public int Step { get; }

        public Seconds Wait { get; }
    }
}
