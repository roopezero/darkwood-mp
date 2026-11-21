namespace DarkwoodMP.World.Scripted
{
    /// An event trigger (the game's EventTrigger) and its events
    /// (GameEvents) in this game, and, on the host, what it knows about the
    /// player who asks about it.
    public readonly struct TriggerFacts
    {
        /// The trigger was found (ObjRef and index resolve).
        public bool Exists { get; init; }

        /// EventTrigger.disabled.
        public bool Disabled { get; init; }

        /// EventTrigger.multipleFire: it fires each time.
        public bool MultipleFire { get; init; }

        /// EventTrigger.fired.
        public bool Fired { get; init; }

        /// EventTrigger.firedExit: its exit ran since it last fired.
        public bool FiredExit { get; init; }

        /// It is an area trigger (EventTrigger.Type.area).
        public bool Area { get; init; }

        /// It has events (EventTrigger.gameEvents isn't null).
        public bool HasEvents { get; init; }

        /// GameEvents.multipleFire: its events run each time.
        public bool EventsMultipleFire { get; init; }

        /// GameEvents.fired.
        public bool EventsFired { get; init; }

        /// A later player gets something of its events
        /// (ScriptedEvents.HasLaterSteps).
        public bool LaterSteps { get; init; }

        /// The host gave the asking player their part already
        /// (OwedParts.Given).
        public bool Given { get; init; }

        /// The type of the first of its requirements the host checks
        /// (itemState, itemTurnedOn, npcStateIsDead) that isn't met here;
        /// null when none fails.
        public string Unmet { get; init; }

        /// The EventTriggers object's name.
        public string Name { get; init; }

        /// The asking player and the EventTriggers object. The rules put
        /// TriggerRules.Reach in it, and its What is the object's name.
        public ReachFacts Reach { get; init; }
    }
}
