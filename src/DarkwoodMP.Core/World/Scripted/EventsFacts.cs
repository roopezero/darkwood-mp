namespace DarkwoodMP.World.Scripted
{
    /// A list of event steps (the game's GameEvents): a trigger's events,
    /// its exit events or a world event.
    public readonly struct EventsFacts
    {
        /// It isn't null.
        public bool Exists { get; init; }

        /// GameEvents.fired.
        public bool Fired { get; init; }

        /// GameEvents.multipleFire: it runs each time.
        public bool MultipleFire { get; init; }

        /// Its object is active in the hierarchy.
        public bool Active { get; init; }

        /// It may run while inactive (not GameEvents.dontRunIfInactive).
        public bool RunsInactive { get; init; }
    }
}
