namespace DarkwoodMP.World.Objects
{
    /// What happens when a shadow on a thing dies in this game.
    public enum ShadowDeath : byte
    {
        /// It dies, as alone, and isn't sent.
        Dies = 0,

        /// A client's own copy: it doesn't die until the host says so.
        Waits,

        /// The host's: it dies, and is sent when sharing.
        DiesForAll,
    }
}
