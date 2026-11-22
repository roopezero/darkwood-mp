namespace DarkwoodMP.World.Scripted
{
    /// One step of a scripted event (a GameEvent), and what the shell's
    /// tables say about its type.
    public readonly struct StepFacts
    {
        /// The step isn't null.
        public bool Exists { get; init; }

        /// GameEvent.disabled.
        public bool Disabled { get; init; }

        /// How a later player's part treats its type.
        public StepType Type { get; init; }

        /// GameEvent.activeModifier, activeModifier2 and relentlessPursuit.
        public bool ActiveModifier { get; init; }

        public bool ActiveModifier2 { get; init; }

        public bool RelentlessPursuit { get; init; }

        /// One of the items it adds is an important item
        /// (InvItem.isImportantItem) or a key (KeyReference).
        public bool SpecialItem { get; init; }

        /// It acts on the player of the game that runs it
        /// (ScriptedEvents.TargetsPlayer).
        public bool TargetsPlayer { get; init; }

        /// Its type is a player's own (ScriptedEvents.IsPlayerStep).
        public bool PlayerType { get; init; }

        /// It only changes how a repeatable area looks or sounds
        /// (EventParts.IsAreaLook).
        public bool AreaLook { get; init; }

        /// Its type is the host's (ScriptedEvents.IsHostStep).
        public bool HostType { get; init; }

        /// A moveInventory between world containers only
        /// (Hideouts.IsWorldMove).
        public bool WorldMove { get; init; }

        /// Its type is displayMessage.
        public bool Message { get; init; }
    }
}
