namespace DarkwoodMP.World.KeptCharacters
{
    /// What a client does with a CharacterLoad.
    public enum LoadStep
    {
        /// Use it.
        Take = 0,

        /// Not from the host, or not a client: nothing.
        Ignore,

        /// For a world we've left: log it and ask again in this one.
        AskAgain,
    }
}
