namespace DarkwoodMP.Combat.Enemies
{
    /// What the client's 1 s sweep does to one of its own characters
    /// (FreezeRules.Sweep).
    public enum SweepStep
    {
        /// Nothing.
        None = 0,

        /// Ours to run after all: unfrozen (NetEnemies.Thaw).
        Thaw,

        /// The host's to run: frozen and hidden.
        Freeze,
    }
}
