namespace DarkwoodMP.Combat.Enemies
{
    /// What the host's scan does with one tracked character
    /// (EnemyTrackRules.Calm).
    public enum CalmStep
    {
        /// Nothing changes.
        Keep = 0,

        /// A hostile NPC: its calm timer is switched off.
        Hostile,

        /// An NPC that has just calmed down: its calm timer starts now.
        StartCalm,

        /// Untracked: destroyed, or an NPC calm for longer than CalmTime.
        Gone,
    }
}
