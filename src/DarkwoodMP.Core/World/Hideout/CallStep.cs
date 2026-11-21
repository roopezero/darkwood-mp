namespace DarkwoodMP.World.Hideout
{
    /// Who calls the porter.
    public enum CallStep : byte
    {
        /// The game's spawnPorter runs here.
        RunGame = 0,

        /// A client's whistle: ask the host (PorterCalled), not the game's.
        Request,
    }
}
