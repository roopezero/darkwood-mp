namespace DarkwoodMP.World.Mornings
{
    /// What the host's clock looks at each frame, on the host with a
    /// Controller and a player.
    public readonly struct ClockFacts
    {
        /// In a morning with its timeFreeze effect on our player
        /// (Controller.isAfterNight and the player's effects).
        public bool Frozen { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// Others share this world (PauseRules.InCoop).
        public bool InCoop { get; init; }

        /// Plugin.Instance.Host.Players exists.
        public bool HasSync { get; init; }

        /// A remote player is out of their morning (MorningRules.IsOut
        /// for any of PlayerSync.Remotes).
        public bool AnyoneOut { get; init; }
    }
}
