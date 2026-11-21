namespace DarkwoodMP.World.Traps
{
    /// What a client knows when its player's progress bar completes
    /// (Player.progressBarCompleted), to say whether the disarm goes to the
    /// host instead of the game's own roll.
    public readonly struct DisarmFacts
    {
        /// Player.disarmingTrap.
        public bool Disarming { get; init; }

        /// NetEnemies.ClientMode.
        public bool ClientMode { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// Player.trapBeingDisarmed isn't null.
        public bool HasTrap { get; init; }

        /// The trap's ObjRef is valid.
        public bool Named { get; init; }

        /// The trap is ours alone (WorldActions.OursAlone).
        public bool OursAlone { get; init; }
    }
}
