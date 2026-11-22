namespace DarkwoodMP.Combat.Hostiles
{
    /// What our game does with the host's answer to our NpcHit.
    public enum NpcHitAnswer
    {
        /// Nothing.
        Nothing,

        /// The host applied it: ScriptedEvents.HitApplied with its result.
        HostApplied,

        /// Only our game has the NPC: our hit and its effects, here.
        OursHere,
    }
}
