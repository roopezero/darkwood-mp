namespace DarkwoodMP.World.Doors
{
    /// The host's check of what a client did to a door (the plugin's
    /// Doors.Allowed), and what a client's door is set back to when the
    /// host refused it (Doors.RollBack). The
    /// first check that fails wins: is the door there, is the player in
    /// reach, then the door's state.
    public static class DoorRules
    {
        /// How far from a door a player may be to use it.
        public const float Reach = ReachRules.Reach;

        /// How far a player may be from a door they hit: a shot or a
        /// flamethrower's flame reaches further than a hand.
        public const float ShotReach = ReachRules.ShotReach;

        /// More damage than this in one hit is more than any weapon does.
        public const float MaxHit = 1000f;

        /// An unlock's how (the message's x): by the story.
        public const float ByStory = 0f;

        /// An unlock's how: with a key. Any other value (2 is a lockpick)
        /// is taken without a check.
        public const float WithKey = 1f;

        /// Host: a client opened the door.
        public static Verdict MayOpen(in DoorFacts door)
        {
            var verdict = Present(door, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (door.Destroyed)
            {
                return Verdict.Refuse(Refusal.DoorBroken);
            }

            if (door.HasLock && door.Locked)
            {
                return Verdict.Refuse(Refusal.DoorLocked);
            }

            if (door.Blocked)
            {
                return Verdict.Refuse(Refusal.DoorBlocked);
            }

            return Verdict.Allow;
        }

        /// Host: a client closed the door. Any state will do.
        public static Verdict MayClose(in DoorFacts door) =>
            Present(door, Reach);

        /// Host: a client's hammer stroke barricaded the door. On a broken
        /// door the strokes rebuild it (Door.setBarricadeState calls
        /// unDestroy): a repair, so a broken door's barricade is allowed.
        public static Verdict MayBarricade(in DoorFacts door)
        {
            var verdict = Present(door, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (door.Barricaded && !door.Destroyed)
            {
                return Verdict.Refuse(Refusal.DoorBarricaded);
            }

            return Verdict.Allow;
        }

        /// Host: a client broke the door's barricade.
        public static Verdict MayDestroyBarricade(in DoorFacts door)
        {
            var verdict = Present(door, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (!door.Barricaded)
            {
                return Verdict.Refuse(Refusal.DoorNotBarricaded);
            }

            return Verdict.Allow;
        }

        /// Host: a client broke the door. A broken door can be broken
        /// again.
        public static Verdict MayDestroy(in DoorFacts door) =>
            Present(door, Reach);

        /// Host: a client hit the door for this much damage. A negative
        /// hit isn't refused (an item's or a vine's is).
        public static Verdict MayHit(in DoorFacts door, float damage)
        {
            var verdict = Present(door, ShotReach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (door.Destroyed)
            {
                return Verdict.Refuse(Refusal.DoorAlreadyBroken);
            }

            if (damage > MaxHit)
            {
                return Verdict.Refuse(
                    Refusal.HitTooLarge,
                    damage.ToString("0"));
            }

            return Verdict.Allow;
        }

        /// Host: a client locked the door. Only the story does that, so
        /// it's always refused, but a far door gets the reach's words.
        public static Verdict MayLock(in DoorFacts door)
        {
            var verdict = Present(door, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            return Verdict.Refuse(Refusal.StoryOnly);
        }

        /// Host: a client unlocked the door, by how (ByStory, WithKey, or
        /// 2 for a lockpick). A lockpick, or any other value, isn't
        /// checked.
        public static Verdict MayUnlock(in DoorFacts door, float how)
        {
            var verdict = Present(door, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (!door.HasLock)
            {
                return Verdict.Refuse(Refusal.DoorHasNoLock);
            }

            if (!door.Locked)
            {
                return Verdict.Refuse(Refusal.DoorNotLocked);
            }

            if (how == ByStory)
            {
                return Verdict.Refuse(Refusal.StoryOnly);
            }

            if (how == WithKey && !door.HasKey)
            {
                return Verdict.Refuse(Refusal.NoKey, door.KeyType);
            }

            return Verdict.Allow;
        }

        /// Host: a client mended the broken door.
        public static Verdict MayUndestroy(in DoorFacts door) =>
            Present(door, Reach);

        /// Host: a client unblocked the door. Only the story does that, so
        /// it's always refused, but a far door gets the reach's words.
        public static Verdict MayUnblock(in DoorFacts door)
        {
            var verdict = Present(door, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            return Verdict.Refuse(Refusal.StoryOnly);
        }

        /// Host: the check for any door action, by its value, with the
        /// message's x (a hit's damage, an unlock's how). A value with no
        /// check of its own (0, say) only needs the door there and in
        /// reach.
        public static Verdict MayAct(
            in DoorFacts door,
            DoorAction action,
            float x)
        {
            switch (action)
            {
                case DoorAction.Open:
                    return MayOpen(door);
                case DoorAction.Barricade:
                    return MayBarricade(door);
                case DoorAction.DestroyBarricade:
                    return MayDestroyBarricade(door);
                case DoorAction.Hit:
                    return MayHit(door, x);
                case DoorAction.Lock:
                    return MayLock(door);
                case DoorAction.Unlock:
                    return MayUnlock(door, x);
                case DoorAction.Unblock:
                    return MayUnblock(door);
                default:
                    return Present(door, Reach);
            }
        }

        /// Client: what to do to our door to match the host's, in the
        /// order the flags' values give. Blocked, health and the
        /// barricade's health are set as they are, whatever this says.
        public static DoorFixes RollBack(DoorStatus host, DoorStatus ours)
        {
            var fixes = DoorFixes.None;
            if (host.Destroyed && !ours.Destroyed)
            {
                fixes |= DoorFixes.Break;
            }
            else if (!host.Destroyed && ours.Destroyed)
            {
                fixes |= DoorFixes.Mend;
            }

            if (host.Locked && !ours.Locked)
            {
                fixes |= DoorFixes.Lock;
            }
            else if (!host.Locked && ours.Locked)
            {
                fixes |= DoorFixes.Unlock;
            }

            if (host.Barricaded && !ours.Barricaded)
            {
                fixes |= DoorFixes.Barricade;
            }
            else if (!host.Barricaded && ours.Barricaded)
            {
                fixes |= DoorFixes.RemoveBarricade;
            }

            if (host.Opened && !ours.Opened)
            {
                fixes |= DoorFixes.Open;
            }
            else if (!host.Opened && ours.Opened)
            {
                fixes |= DoorFixes.Close;
            }

            return fixes;
        }

        /// The door is there and the requester within reach of it.
        static Verdict Present(in DoorFacts door, float reach)
        {
            if (!door.Exists)
            {
                return Verdict.Refuse(Refusal.NoSuchDoor);
            }

            return ReachRules.InReach(door.Requester with { Reach = reach });
        }
    }
}
