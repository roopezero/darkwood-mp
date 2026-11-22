namespace DarkwoodMP.World.Doors
{
    /// Whether this game sends what a door call did (the plugin's Doors,
    /// from its DoorPatches). WorldActions.Send then filters a
    /// client's to its own player's.
    public static class DoorSendRules
    {
        /// Open, close, barricade, destroy barricade, destroy, undestroy
        /// and hit: sent from the outermost call, while sharing, when the
        /// door changed.
        public static bool SendsChange(in DoorSendFacts send) =>
            send.Outermost && send.Sharing && !send.Before.Equals(send.After);

        /// Lock and unlock (Door.lockMe, Door.unlock): sent from the
        /// outermost call while sharing, changed or not.
        public static bool SendsLock(bool outermost, bool sharing) =>
            outermost && sharing;

        /// The story's unblock: sent when it cleared the block here,
        /// outside every patched call, while sharing.
        public static bool SendsUnblock(in UnblockFacts unblock) =>
            unblock.WasBlocked
            && !unblock.Blocked
            && unblock.Depth == 0
            && unblock.Sharing;
    }
}
