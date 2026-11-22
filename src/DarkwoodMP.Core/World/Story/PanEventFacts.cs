namespace DarkwoodMP.World.Story
{
    /// A world event is about to fire (EpilogueSync.WorldEventFired).
    public readonly struct PanEventFacts
    {
        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// The event's type is the pan's (EpilogueRules.PanEvent).
        public bool IsPan { get; init; }

        /// A session is set and Active.
        public bool InSession { get; init; }

        /// The pan is released (EpilogueSync._panGo).
        public bool Released { get; init; }

        /// The pan ran since it was released (EpilogueSync._ownPan).
        public bool PanRan { get; init; }
    }
}
