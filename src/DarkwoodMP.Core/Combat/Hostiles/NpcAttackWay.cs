namespace DarkwoodMP.Combat.Hostiles
{
    /// What becomes of a scripted attack by an NPC of ours.
    public enum NpcAttackWay
    {
        /// The game's own attack runs.
        Game,

        /// Skipped: it is the host's, or the host sent its copy already.
        Skip,

        /// Skipped here and sent to the host as an NpcAttack request.
        Send,
    }
}
