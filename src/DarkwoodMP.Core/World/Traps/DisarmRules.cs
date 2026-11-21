namespace DarkwoodMP.World.Traps
{
    /// Disarming a shared trap: the host's roll (the plugin's
    /// WorldActions.OnAction), a client's handing of its disarm to the host
    /// (ClientDisarmPatch), and the crouch co-op adds (DisarmAnimation).
    public static class DisarmRules
    {
        /// What the host's answer says when the roll worked.
        public const string Worked = "1";

        /// What the host's answer says when the roll failed.
        public const string Failed = "0";

        /// Host: whether a client's disarm worked. The roll is
        /// Random.Range(0f, 1f), made by the shell; the chance is the
        /// trap's Item.disarmChance. Strictly more than the roll, as the
        /// game's own roll: a roll of exactly the chance fails. A trap
        /// with no Item never works. The shell draws the roll only for a
        /// trap with an Item and passes 0 otherwise, as the old
        /// `item != null && ...` never rolled for one without, so Unity's
        /// random numbers come out in the same order.
        public static bool Works(bool item, float chance, float roll)
        {
            return item && chance > roll;
        }

        /// Client: whether its player's finished disarm goes to the host as
        /// a TrapDisarm request instead of the game's own roll. Not for a
        /// trap that isn't shared (no valid ObjRef) or is ours alone: the
        /// game rolls those, as alone.
        public static bool AsksHost(in DisarmFacts disarm)
        {
            if (!disarm.Disarming || !disarm.ClientMode || !disarm.Sharing)
            {
                return false;
            }

            if (!disarm.HasTrap)
            {
                return false;
            }

            return disarm.Named && !disarm.OursAlone;
        }

        /// Client: what to do with the host's answer to its disarm. A yes
        /// whose result isn't "1" fails, so an away requester's (result
        /// "") ends as a failed disarm.
        public static DisarmOutcome Outcome(bool ok, string result)
        {
            if (ok && result == Worked)
            {
                return DisarmOutcome.Disarmed;
            }

            if (ok)
            {
                return DisarmOutcome.Failed;
            }

            return DisarmOutcome.Nothing;
        }

        /// Whether co-op makes the player crouch for this disarm, so the
        /// others see it (DisarmCrouchPatch): in co-op, disarming an item
        /// with a Trigger the game doesn't crouch for already.
        public static bool Crouches(in CrouchFacts disarm)
        {
            if (!disarm.InCoop || !disarm.HasItem || !disarm.Disarming)
            {
                return false;
            }

            return disarm.HasTrigger && !disarm.BendsDown;
        }
    }
}
