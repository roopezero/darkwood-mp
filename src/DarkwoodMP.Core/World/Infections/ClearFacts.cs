namespace DarkwoodMP.World.Infections
{
    /// What a game knows when one of its infection patches has been told
    /// to fade (Infection.disappear).
    public readonly struct ClearFacts
    {
        /// It was fading already before the call (Infection.disappearing).
        public bool WasFading { get; init; }

        /// It is fading after the call.
        public bool Fading { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// This game is a client (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// The local player's own melee swing is running
        /// (LiquidLitPatch.OwnSwing).
        public bool OwnSwing { get; init; }
    }
}
