namespace DarkwoodMP.Hosting
{
    /// What the host box looks at once the world is ready.
    public readonly struct BeginFacts
    {
        /// A session is running already (Session.Active).
        public bool InSession { get; init; }

        /// Whether the player may host this world.
        public HostFacts Host { get; init; }

        /// The box asked to host with Steam.
        public bool SteamAsked { get; init; }

        /// Steam's backend is up (Plugin's _steam is an IGuiSection).
        public bool SteamUp { get; init; }
    }
}
