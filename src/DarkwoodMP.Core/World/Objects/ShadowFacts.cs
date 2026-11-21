namespace DarkwoodMP.World.Objects
{
    /// What a game knows when the shadow on a shadow-covered thing dies
    /// (ShadowArmor.die).
    public readonly struct ShadowFacts
    {
        /// The call's instant: the save being loaded says it is dead.
        public bool Instant { get; init; }

        /// The shadow is on an Item that isn't a Character.
        public bool OnItem { get; init; }

        /// Session.Instance isn't null.
        public bool InSession { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        /// Coop.Guards.Replaying: the death is the host's, applied here.
        public bool Replaying { get; init; }
    }
}
