namespace DarkwoodMP.Sync.Players
{
    /// What a PlayerHurt message says happened, as its kind byte.
    public enum HurtKind : byte
    {
        /// Our player bled: a blood splat.
        Blood = 1,

        /// They started burning: flames and a fire sound on their puppet.
        Burning = 2,

        /// They stopped burning.
        Stopped = 3,
    }
}
