namespace DarkwoodMP.World.Saves
{
    /// A player as the save book names them: their key (empty without one)
    /// and their name.
    public readonly struct BookPlayer
    {
        public string Key { get; init; }
        public string Name { get; init; }
    }
}
