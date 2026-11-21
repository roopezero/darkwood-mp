namespace DarkwoodMP.World.Scripted
{
    /// A once-only trigger our player sets off now, for whether it runs
    /// as a later player's part (ScriptedEvents.LaterPart).
    public readonly struct LaterFacts
    {
        /// The trigger and its EventTriggers exist, and its type is the
        /// type set off.
        public bool Matches { get; init; }

        /// Our player is owed its part of it (TriggerRules.IsOwed).
        public bool Owed { get; init; }

        /// A session exists and is active, and Coop.Guards.Sharing.
        public bool Active { get; init; }

        public bool Sharing { get; init; }

        /// The part being run now (ScriptedEvents.Current).
        public EventPart Current { get; init; }

        /// Our own player is setting it off (ScriptedEvents.SettingOff).
        public bool SettingOff { get; init; }

        /// EventTrigger.disabled, after its delay.
        public bool Disabled { get; init; }

        /// Its requirements hold (EventTrigger.requirementsMet).
        public bool Met { get; init; }

        /// We asked the host for it and have no answer yet.
        public bool Asking { get; init; }
    }
}
