namespace DarkwoodMP.World.Hideout
{
    /// Hideout ovens (ExperienceMachine) while playing together: each
    /// player sleeps at their own, and lighting one puts out the player's
    /// old one unless someone else still has it as theirs.
    public static class OvenRules
    {
        /// Whether setExperienceMachine is ours to do (Hideouts.Light)
        /// rather than the game's: an oven to light, not replaying,
        /// together.
        public static bool TakesOver(in OvenCallFacts call)
        {
            if (!call.HasOven || !call.Enable || call.Replaying)
            {
                return false;
            }

            return call.Together;
        }

        /// The host, checking a joiner's lit oven, in its order. The
        /// reach refusal is ReachRules.InReach's. Any Location holding
        /// the oven counts as a hideout. The joiner's claim of its old oven
        /// isn't checked.
        public static Verdict MayLight(in OvenFacts oven)
        {
            if (!oven.Exists)
            {
                return Verdict.Refuse(Refusal.NoSuchOven);
            }

            var reach = ReachRules.InReach(oven.Reach);
            if (reach.Refused)
            {
                return reach;
            }

            if (!oven.AtLocation)
            {
                return Verdict.Refuse(Refusal.OvenNotAtHideout);
            }

            return Verdict.Allow;
        }

        /// The host: whether the old oven goes out.
        public static bool PutsOut(in PutOutFacts old)
        {
            if (!old.HasOld || old.OldIsLit)
            {
                return false;
            }

            return !old.OldIsHosts && !old.OldIsOthers;
        }

        /// A client: what a refusal undoes. It is put out only if it
        /// wasn't on before.
        public static OvenRollback Rollback(in RollbackFacts oven)
        {
            if (oven.Allowed || !oven.Exists)
            {
                return default;
            }

            return new OvenRollback
            {
                PutOut = !oven.WasOn,
                GiveBack = oven.StillOurs,
            };
        }

        /// A client: the host's lit oven, and the one it put out.
        public static OvenChange FromHost(in HostOvenFacts oven)
        {
            bool other = oven.HasOther && !oven.OtherIsLit;
            return new OvenChange
            {
                Light = oven.Found && !oven.On,
                PutOut = other && oven.HasPlayer && !oven.OtherIsOurs,
            };
        }

        /// The host: a joiner who connects starts with the host player's
        /// oven as theirs (they loaded the host's save).
        public static bool GivesHostsOven(bool host, bool player)
        {
            return host && player;
        }
    }
}
