namespace DarkwoodMP.World.Breaking
{
    /// What a game knows after a tracked explosive's Explodes.onActivate.
    public readonly struct ExplodeSendFacts
    {
        /// It was the outermost patched call (WorldActions.Outermost).
        public bool Outermost { get; init; }

        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// It has gone off now (Explodes.activated).
        public bool Activated { get; init; }

        /// This game is a client (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }
    }
}
