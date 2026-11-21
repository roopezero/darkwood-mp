namespace DarkwoodMP.World.Traps
{
    /// What a client knows about a trap going off, to say whether what it
    /// spawns (a vine trap's vine) waits for the host's yes.
    public readonly struct HoldFacts
    {
        /// Coop.Guards.Depth, with this call counted.
        public int Depth { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// Trigger.loadedFromSave.
        public bool LoadedFromSave { get; init; }

        /// Trigger.prefabToSpawn isn't null.
        public bool Spawns { get; init; }

        /// Our player set it off (TrapRules.IsOurs).
        public bool Ours { get; init; }
    }
}
