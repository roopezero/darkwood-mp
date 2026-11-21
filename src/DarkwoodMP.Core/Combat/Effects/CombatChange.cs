namespace DarkwoodMP.Combat.Effects
{
    /// What the host does with a joiner's combat state on a tick
    /// (EffectRules.Change).
    public enum CombatChange : byte
    {
        /// Same as last sent: nothing.
        Same = 0,

        /// Changed, for the test dummy: kept, not sent.
        Kept,

        /// Changed: kept, and CombatMusic sent to that joiner.
        Sent,
    }
}
