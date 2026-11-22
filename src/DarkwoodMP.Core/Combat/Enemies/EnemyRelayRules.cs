namespace DarkwoodMP.Combat.Enemies
{
    /// What the host sends each client about the enemies it runs (the
    /// plugin's NetEnemies.Update, HostUpdate, HostDeadSince and
    /// ArmorSync).
    public static class EnemyRelayRules
    {
        /// How far from a player's stand-in the host's enemies are sent to
        /// them, in 3D.
        public const float RelevantRange = 3000f;

        /// How often the host sends each client its enemies' states.
        public const float StateInterval = 0.05f;

        /// How long a dead enemy is still sent before it's left where it
        /// fell (scaled time).
        public const float CorpseLinger = 6f;

        /// Corpses sent to one client in one state pass at most.
        public const int CorpsesPerPass = 4;

        /// The host's part runs this frame: a host with a peer, in its
        /// world.
        public static bool HostRuns(bool host, int peers, bool inWorld)
        {
            return host && peers > 0 && inWorld;
        }

        /// A client is served in this pass: it has a stand-in, and isn't
        /// away. Away, it gets nothing, not even despawns.
        public static bool Serves(bool standIn, bool away)
        {
            return standIn && !away;
        }

        /// The enemy's death is timed now (Tracked.DiedAt = Time.time): out
        /// of play, and not timed yet. Only asked inside a served client's
        /// pass.
        public static bool Dies(bool inPlay, bool timed)
        {
            return !inPlay && !timed;
        }

        /// What one served client is sent about one enemy, in the host's
        /// order of checks. Near means active on the host and under
        /// RelevantRange in 3D.
        public static RelayStep Choose(in RelayFacts e)
        {
            if (!e.Exists)
            {
                return RelayStep.Skip;
            }

            bool done = e.Dead && e.SinceDeath.Value > CorpseLinger;
            bool near = e.Active && Near(e.Enemy, e.StandIn);
            if (e.Dead && e.CorpseKept)
            {
                return RelayStep.Skip;
            }

            if (done && !e.Known)
            {
                if (near && e.CorpsesSent < CorpsesPerPass)
                {
                    return RelayStep.SendCorpse;
                }

                return RelayStep.Skip;
            }

            if (done || !near)
            {
                return Dropped(e);
            }

            if (e.Known)
            {
                return RelayStep.State;
            }

            return RelayStep.Spawn;
        }

        /// The batch of states goes only when it holds one.
        public static bool SendsBatch(int count)
        {
            return count != 0;
        }

        /// After a drop that keeps the corpse, its loot is sent if the
        /// corpse was new to the client's kept ones.
        public static bool SendsLoot(bool corpse, bool added)
        {
            return corpse && added;
        }

        /// Host: an enemy that died at least least ago (Time.time), for
        /// HostDeadSince.
        public static bool DeadSince(bool dead, Seconds since, Seconds least)
        {
            return dead && since.Value >= least.Value;
        }

        /// Host: the clients that know an enemy are told when its shadow
        /// armour (a ShadowArmor that is enabled) differs from what they
        /// were told.
        public static bool ArmorChanged(bool wears, bool told)
        {
            return wears != told;
        }

        static RelayStep Dropped(in RelayFacts e)
        {
            if (!e.Known)
            {
                return RelayStep.Skip;
            }

            if (e.Dead)
            {
                return RelayStep.DropCorpse;
            }

            return RelayStep.Drop;
        }

        /// As Unity's (a - b).sqrMagnitude < range * range.
        static bool Near(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return x * x + y * y + z * z < RelevantRange * RelevantRange;
        }
    }
}
