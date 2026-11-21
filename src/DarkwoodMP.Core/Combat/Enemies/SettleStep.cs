namespace DarkwoodMP.Combat.Enemies
{
    /// How a dead copy is made a corpse once its death has played
    /// (CopyStateRules.SettleAs).
    public enum SettleStep
    {
        /// The game's three corpse steps (destroyComponents,
        /// destroyComponents2, setDeathCollider).
        Corpse = 0,

        /// Cut in half on the host: its upper part is no corpse
        /// (SettleCut).
        Cut,

        /// No sprite animator (the worms): only the collider, as dying
        /// does to it; never searchable, as on the host.
        Collider,
    }
}
