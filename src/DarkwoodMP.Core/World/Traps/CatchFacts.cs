namespace DarkwoodMP.World.Traps
{
    /// What a game knows about a trap that just went off
    /// (Trigger.OnAfterTrigger), to say whether its own player set it off.
    public readonly struct CatchFacts
    {
        /// Trigger.setByPlayer: a trap a player set down.
        public bool SetByPlayer { get; init; }

        /// The trap has an Item: a trap, not a level's trigger zone.
        public bool HasItem { get; init; }

        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// The trap is ours alone (WorldActions.OursAlone): a dream or a
        /// place of our own.
        public bool OursAlone { get; init; }

        /// What set it off has a Player: our own player.
        public bool ByPlayer { get; init; }

        /// What set it off has a ThrownItem: something a player threw.
        public bool ByThrown { get; init; }
    }
}
