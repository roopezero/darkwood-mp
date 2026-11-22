namespace DarkwoodMP.World.Scripted
{
    /// A world event fired by name (Events.fireWorldEvent), for whether it
    /// is our player's to share.
    public readonly struct WorldEventFacts
    {
        /// The part being run now (EventParts.Current).
        public EventPart Current { get; init; }

        /// Coop.Guards.Replaying and Coop.Guards.Sharing.
        public bool Replaying { get; init; }

        public bool Sharing { get; init; }

        /// The host is running a world event it sent as an animation's
        /// (ScriptedEvents.AnimationSent).
        public bool AnimationSent { get; init; }

        /// It is the epilogue's pan (EpilogueSync.PanEvent).
        public bool Pan { get; init; }

        /// It has a name and this game has it.
        public bool Known { get; init; }

        /// Its GameEvents.fired and multipleFire.
        public bool Fired { get; init; }

        public bool MultipleFire { get; init; }
    }
}
