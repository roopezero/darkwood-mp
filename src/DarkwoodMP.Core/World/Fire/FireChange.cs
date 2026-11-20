namespace DarkwoodMP.World.Fire
{
    /// What a client does with the host's Burning.
    public enum FireChange : byte
    {
        Nothing = 0,

        /// Add a Burn with the host's burn time, as it is.
        Light,

        /// Put its Burn out (Burn.stop).
        PutOut,
    }
}
