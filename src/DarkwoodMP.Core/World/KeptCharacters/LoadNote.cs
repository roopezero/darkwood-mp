namespace DarkwoodMP.World.KeptCharacters
{
    /// Which line the client logs once its character is placed.
    public enum LoadNote
    {
        /// "A new character for you in the host's prologue"
        Prologue = 0,

        /// "Your character in this world is back"
        Own,

        /// "Your character from the last chapter is back, beside the host"
        LastChapter,

        /// "A new character for you in this world"
        New,

        /// "You start as a copy of the host's character"
        HostCopy,
    }
}
