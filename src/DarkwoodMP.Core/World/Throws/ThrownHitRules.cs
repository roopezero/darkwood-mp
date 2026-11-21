namespace DarkwoodMP.World.Throws
{
    /// Whether a hit comes from something thrown (the plugin's ThrownHits
    /// and ClientExplosionSelfHitPatch). A client's game sends no hits on
    /// the host's enemies from anything thrown, and leaves an explosion
    /// that catches its own player to the host's copy to report.
    public static class ThrownHitRules
    {
        /// Whether a hit happening now comes from something thrown
        /// (ThrownHits.Now): inside a thrown thing's impact or explosion,
        /// or from a flame under something a thrown thing spawned.
        public static bool Now(in ThrownHitFacts hit)
        {
            if (hit.Depth > 0)
            {
                return true;
            }

            if (!hit.Flame)
            {
                return false;
            }

            return hit.UnderSpawned;
        }

        /// Client: whether a hit on our own player may be an explosion the
        /// host reports through our proxy (ClientExplosionSelfHitPatch
        /// .HostReports, all but its look at the stack): in client mode,
        /// inside something thrown, with no flame touching, and not away
        /// in a place of our own (explosions there happen only here). The
        /// shell then asks the stack for an Explodes method, last, since
        /// that's the costly part.
        public static bool HostMayReport(in SelfHitFacts hit)
        {
            if (!hit.ClientMode || hit.Depth <= 0)
            {
                return false;
            }

            return !hit.Flame && !hit.Away;
        }

        /// Client: whether an effect on a character may be one the host
        /// reports (ClientExplosionSelfEffectPatch): inside something
        /// thrown, on our own player's effects, and HostMayReport. The
        /// stack is asked after this, as for the hit.
        public static bool HostMayReportEffect(
            in SelfHitFacts hit,
            bool ours)
        {
            if (hit.Depth <= 0 || !ours)
            {
                return false;
            }

            return HostMayReport(hit);
        }
    }
}
