namespace DarkwoodMP.Combat.Damage
{
    /// Hits on remote players' stand-ins and how their players take them
    /// (the plugin's Combat/PlayerDamage).
    public static class DamageRules
    {
        /// At most this many of a hit's effects travel with it.
        public const int MostEffects = 16;

        /// Host: what becomes of a hit on a stand-in, checked in this
        /// order: a friendly NPC's, the dummy's, then whether we host.
        public static ProxyHit Send(in ProxyHitFacts hit)
        {
            if (hit.ByNpc && !hit.NpcIsEnemy)
            {
                return ProxyHit.LeftToTheirCopy;
            }

            if (hit.ToDummy)
            {
                return ProxyHit.ToDummy;
            }

            if (!hit.Host)
            {
                return ProxyHit.NotSent;
            }

            return ProxyHit.Sent;
        }

        /// How many of the effects are written: no more than MostEffects.
        /// The shell gives 0 for a null list.
        public static int EffectsSent(int count) =>
            Floats.Min(count, MostEffects);

        /// Client: whether the host's hit is read at all (a client, and
        /// the message is from the host).
        public static bool Reads(bool client, bool fromHost) =>
            client && fromHost;

        /// Client: whether our player takes the host's hit (Player.Instance
        /// exists and is alive). The numbers aren't checked: the host is
        /// trusted.
        public static bool Takes(bool player, bool alive) =>
            player && alive;
    }
}
