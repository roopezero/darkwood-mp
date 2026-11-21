namespace DarkwoodMP.World.Fire
{
    /// What is burning, as the Burning message's u8 says it (the plugin's
    /// FireSync.What). The values are on the wire, so they never change.
    public enum FireKind : byte
    {
        /// Nothing that is shared: a player, a stand-in, anything else.
        None = 0,

        /// A Character: an enemy, or an animal.
        Enemy = 1,

        Door = 2,

        Window = 3,

        Item = 4,
    }
}
