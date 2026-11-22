namespace DarkwoodMP.World.Objects
{
    /// What a client knows when its player uses a feeder (Feeder.activate),
    /// to say whether the use waits for the host's yes.
    public readonly struct FeederAskFacts
    {
        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// NetEnemies.ClientMode.
        public bool ClientMode { get; init; }

        /// The feeder's ObjRef is valid.
        public bool Named { get; init; }

        /// The feeder is ours alone (WorldActions.OursAlone).
        public bool OursAlone { get; init; }
    }
}
