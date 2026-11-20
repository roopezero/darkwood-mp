namespace DarkwoodMP.World.Joining
{
    /// The map test's "hold-joiners": for Secs from the first joiner's ask,
    /// the host answers as if it watched the intro film. Secs 0 is off;
    /// From below zero is not started.
    public readonly struct TestHold
    {
        public float Secs { get; init; }

        /// Time.realtimeSinceStartup at the first ask.
        public Seconds From { get; init; }

        /// The hold as it is before any test sets it.
        public static TestHold Off => new TestHold { From = new Seconds(-1f) };
    }
}
