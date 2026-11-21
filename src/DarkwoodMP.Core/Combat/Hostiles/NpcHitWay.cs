namespace DarkwoodMP.Combat.Hostiles
{
    /// What becomes of our player's hit on an NPC of ours.
    public enum NpcHitWay
    {
        /// The game applies it here.
        Game,

        /// The game applies it here, and the shell logs that the hit is
        /// ours (a dead NPC, a night trader or one only we have).
        Ours,

        /// Dropped: the host's copy of the throw hits its NPC. Marked as
        /// left to the host, nothing sent.
        Thrown,

        /// Sent to the host as an NpcHit request, a splat shown, dropped
        /// here and marked as left to the host.
        Sent,
    }
}
