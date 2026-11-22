namespace DarkwoodMP.World.Breaking
{
    /// Breakable items and explosives: the host's checks of a client's
    /// ItemHit, ItemBroken, ItemRepaired and Explode (the plugin's
    /// WorldActions.Allowed), and a client's apply of the host's ItemHit
    /// (WorldActions.OnAction).
    public static class BreakActionRules
    {
        /// The most damage any weapon does in one hit.
        public const float HitCap = 1000f;

        /// Host: whether a client's player could have hit this item: here,
        /// within a shot's reach, not broken, and a hit from 0 to HitCap. A
        /// hit that isn't a number passes the damage check.
        public static Verdict MayHit(in BreakFacts item)
        {
            if (!item.Exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            var reach = InReach(item.Requester, ReachRules.ShotReach);
            if (reach.Refused)
            {
                return reach;
            }

            if (item.Destroyed)
            {
                return Verdict.Refuse(Refusal.AlreadyBroken);
            }

            if (item.Damage > HitCap || item.Damage < 0f)
            {
                return Verdict.Refuse(
                    Refusal.HitTooLarge,
                    item.Damage.ToString("0"));
            }

            return Verdict.Allow;
        }

        /// Host: whether a client's game could have broken this item
        /// without a hit: here, in reach, not broken, and destructible.
        public static Verdict MayBreak(in BreakFacts item)
        {
            var near = HereInReach(item);
            if (near.Refused)
            {
                return near;
            }

            if (item.Destroyed)
            {
                return Verdict.Refuse(Refusal.AlreadyBroken);
            }

            if (!item.Destructible)
            {
                return Verdict.Refuse(Refusal.Unbreakable);
            }

            return Verdict.Allow;
        }

        /// Host: whether a client's player could have repaired this item:
        /// here, in reach, and broken.
        public static Verdict MayRepair(in BreakFacts item)
        {
            var near = HereInReach(item);
            if (near.Refused)
            {
                return near;
            }

            if (!item.Destroyed)
            {
                return Verdict.Refuse(Refusal.NotBroken);
            }

            return Verdict.Allow;
        }

        /// Host: an action value from ItemHit (51) up that no branch
        /// before names (anything but 51 to 53, 61, 90 to 93, 130 and
        /// 131): only that the Item is here and in reach. Only a forged
        /// message sends one; the host then changes nothing, but passes it
        /// on to the others.
        public static Verdict MayDoUnknown(in BreakFacts item)
        {
            return HereInReach(item);
        }

        /// Host: whether a client's game could have set off this explosive:
        /// here and not an enemy's, not gone off already, and within a
        /// shot's reach.
        public static Verdict MayExplode(in ExplodeRequestFacts thing)
        {
            if (!thing.Exists || thing.OnCharacter)
            {
                return Verdict.Refuse(Refusal.ExplosiveGone);
            }

            if (thing.WentOff)
            {
                return Verdict.Refuse(Refusal.AlreadyWentOff);
            }

            return InReach(thing.Requester, ReachRules.ShotReach);
        }

        /// Client, applying the host's ItemHit to an item with no shadow
        /// over it: the health to set before the hit, so that it ends at
        /// the health the host's hit left. Both are cut to whole numbers
        /// first, as the game's int health.
        public static int HealthBeforeHit(float after, float damage)
        {
            return (int)after + (int)damage;
        }

        static Verdict HereInReach(in BreakFacts item)
        {
            if (!item.Exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            return InReach(item.Requester, ReachRules.Reach);
        }

        static Verdict InReach(in ReachFacts requester, float reach)
        {
            return ReachRules.InReach(requester with { Reach = reach });
        }
    }
}
