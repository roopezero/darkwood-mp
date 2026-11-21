namespace DarkwoodMP.World.Scripted
{
    /// One step of the events a client is about to run, for when a trip
    /// in them lands.
    public readonly struct TripStep
    {
        /// The step isn't null, and GameEvent.disabled.
        public bool Exists { get; init; }

        public bool Disabled { get; init; }

        /// GameEvent.delay: how long after the step before it this one
        /// starts.
        public Seconds Delay { get; init; }

        /// It is a transportPlayerToObject without activeModifier (a
        /// trip, not a new chapter).
        public bool Trip { get; init; }

        /// It has a target to go to (its first target isn't null).
        public bool HasTarget { get; init; }

        /// GameEvent.floatValue: how long the screen fades to black first.
        public Seconds Fade { get; init; }
    }
}
