namespace DarkwoodMP.World.Building
{
    /// What Constructible.construct does, decided as it starts.
    public enum BuildStart : byte
    {
        /// The game builds, and nothing is shared.
        Game = 0,

        /// A client already waiting for the host's answer about this one:
        /// the build is swallowed, nothing taken.
        Swallow,

        /// A client: find the option, then ConstructionRules.Asks.
        Client,

        /// The host: one call deeper (Coop.Guards.Depth), the game builds,
        /// and the outermost build is sent after.
        Host,
    }
}
