namespace DarkwoodMP
{
    /// A timer that runs every so often: whether it is due now, and when it
    /// is next due. A rule gives the time; the caller keeps Next.
    public readonly struct Tick
    {
        public bool Due { get; init; }

        /// When it is next due: now plus the period when it ran, else the
        /// time it was given.
        public Seconds Next { get; init; }

        /// Due unless now is before next (so at next exactly it runs).
        public static Tick At(Seconds now, Seconds next, Seconds every)
        {
            if (now < next)
            {
                return new Tick { Due = false, Next = next };
            }

            return new Tick
            {
                Due = true,
                Next = new Seconds(now.Value + every.Value),
            };
        }
    }
}
