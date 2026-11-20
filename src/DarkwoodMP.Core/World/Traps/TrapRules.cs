namespace DarkwoodMP.World.Traps
{
    /// Traps: the host's checks of a client's TrapFired, TrapSwitched and
    /// TrapDisarm (the plugin's WorldActions.Allowed), and a client's side
    /// of a trap going off (TrapFiredPatch, ClientTrapIgnoresCharacters).
    public static class TrapRules
    {
        /// Host: whether a client's player could have set this trap off.
        /// In order: the trap is here, in reach, not refused by the map
        /// test's trap-refuse (only while it is still active), and active.
        public static Verdict MayFire(in TrapFacts trap)
        {
            if (!trap.Exists)
            {
                return Verdict.Refuse(Refusal.TrapGone);
            }

            var reach = InReach(trap);
            if (reach.Refused)
            {
                return reach;
            }

            if (trap.Active && trap.Test == TrapTest.Race)
            {
                return Verdict.Refuse(Refusal.TestTrapWentOff);
            }

            if (trap.Active && trap.Test != TrapTest.None)
            {
                return Verdict.Refuse(Refusal.TestTrapGone);
            }

            if (!trap.Active)
            {
                return Verdict.Refuse(Refusal.TrapWentOff);
            }

            return Verdict.Allow;
        }

        /// Host: whether a client's player could have switched this trap
        /// (Item.switchTriggerState). Only that it is here and in reach:
        /// there is no state check.
        public static Verdict MaySwitch(in TrapFacts trap)
        {
            if (!trap.Exists)
            {
                return Verdict.Refuse(Refusal.TrapGone);
            }

            return InReach(trap);
        }

        /// Host: whether a client's player could have disarmed this trap:
        /// here, in reach, still active and one that can be disarmed.
        public static Verdict MayDisarm(in TrapFacts trap)
        {
            if (!trap.Exists)
            {
                return Verdict.Refuse(Refusal.TrapGone);
            }

            var reach = InReach(trap);
            if (reach.Refused)
            {
                return reach;
            }

            if (!trap.Active || !trap.CanDisarm)
            {
                return Verdict.Refuse(Refusal.CantDisarm);
            }

            return Verdict.Allow;
        }

        /// Whether this game's own player, or something it threw, set the
        /// trap off (TrapFiredPatch.Ours). A level's trigger zone (no
        /// Item, not set by a player) never is. The host counts every trap
        /// as its own; a client counts only those set off by a Player or a
        /// ThrownItem, and none that is ours alone.
        public static bool IsOurs(in CatchFacts trap)
        {
            if (!trap.SetByPlayer && !trap.HasItem)
            {
                return false;
            }

            if (!trap.Client)
            {
                return true;
            }

            if (trap.OursAlone)
            {
                return false;
            }

            return trap.ByPlayer || trap.ByThrown;
        }

        /// Client: whether what the trap spawns waits for the host's yes
        /// (TrapFiredPatch's prefix): the outermost call, while sharing, a
        /// trap not loaded from the save, that spawns something, set off
        /// by us.
        public static bool HoldsSpawn(in HoldFacts trap)
        {
            if (trap.Depth != 1 || !trap.Sharing || !trap.Client)
            {
                return false;
            }

            if (trap.LoadedFromSave)
            {
                return false;
            }

            return trap.Spawns && trap.Ours;
        }

        /// Client: whether a held vine holds what set the trap off: a
        /// chain trap (Trigger.isChainTrap) that connects its chain, set off
        /// by a collider. Otherwise the vine is spawned untargeted.
        public static bool TargetsCatch(bool chain, bool connects, bool hit)
        {
            return chain && connects && hit;
        }

        /// Client: the health a trap took from our player when it bit, to
        /// give back if the host refuses. Nothing unless a Player set it
        /// off and its health before the bite was noted (above 0).
        public static float HealthLost(bool player, float before, float now)
        {
            if (player && before > 0f)
            {
                return before - now;
            }

            return 0f;
        }

        /// Client, when the host refused our trap: the player's health with
        /// what the bite took given back, capped at the most it can be.
        /// Only for a bite that took some, while the player is alive.
        public static float HealthGivenBack(float health, float lost, float max)
        {
            if (lost > 0f && health > 0f)
            {
                return Min(max, health + lost);
            }

            return health;
        }

        /// Client: whether a trap ignores what touched it here
        /// (ClientTrapIgnoresCharactersPatch): the host's enemies, and our
        /// frozen copies of them, set traps off in the host's game.
        public static bool Ignores(in TouchFacts touch)
        {
            if (!touch.ClientMode || !touch.HasCollider)
            {
                return false;
            }

            if (!touch.Character)
            {
                return false;
            }

            return touch.Frozen || touch.Puppet;
        }

        static Verdict InReach(in TrapFacts trap)
        {
            return ReachRules.InReach(
                trap.Requester with { Reach = ReachRules.Reach });
        }

        /// As Mathf.Min: b unless a is smaller.
        static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }
    }
}
