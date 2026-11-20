namespace DarkwoodMP.World.KeptCharacters
{
    /// Where our character is put when it comes.
    public enum StandStep
    {
        /// Where the record (or the host's save) put it.
        Stay = 0,

        /// At the spawn the host sent.
        AtSpawn,

        /// Made new (Fresh), which stands it at a non-zero spawn.
        Fresh,
    }
}
