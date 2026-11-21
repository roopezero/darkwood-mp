namespace DarkwoodMP.World.Locks
{
    /// Locks on things that aren't doors, containers and padlocks: the
    /// host's checks (the plugin's WorldActions.Allowed) and its roll for a
    /// crate's pick (WorldActions.OnAction).
    public static class LockRules
    {
        /// How a lock was opened, in the action's x: by the story
        /// (Locked.unlock called by itself).
        public const float ByStory = 0f;

        /// With a key (Locked.tryToOpenWithKey).
        public const float ByKey = 1f;

        /// With a lockpick (Locked.tryToLockpick). Not checked: the host
        /// passes it, and any other value, unchecked.
        public const float ByLockpick = 2f;

        /// Host: whether a client's player could have opened this lock: it
        /// is here, in reach and locked, not opened as only the story
        /// does, and with a key, one somebody has.
        public static Verdict MayUnlock(in LockFacts lck)
        {
            if (!lck.Exists)
            {
                return Verdict.Refuse(Refusal.NoSuchLock);
            }

            var reach = InReach(lck.Requester);
            if (reach.Refused)
            {
                return reach;
            }

            if (!lck.Locked)
            {
                return Verdict.Refuse(Refusal.NotLocked);
            }

            if (lck.How == ByStory)
            {
                return Verdict.Refuse(Refusal.StoryOnly);
            }

            if (lck.How == ByKey && !lck.KeyKnown)
            {
                return Verdict.Refuse(Refusal.NoKey, lck.KeyType);
            }

            return Verdict.Allow;
        }

        /// Host: whether a client's player could have opened this
        /// container. A crate that isn't picked open yet is the host's roll.
        /// The reach is checked last.
        public static Verdict MaySearch(in SearchFacts box)
        {
            if (!box.Exists)
            {
                return Verdict.Refuse(Refusal.NoSuchThing);
            }

            if (box.Crate && !box.Searched)
            {
                return Verdict.Refuse(Refusal.HostRollsCrates);
            }

            if (!box.HasInventory)
            {
                return Verdict.Refuse(Refusal.NothingToSearch);
            }

            return InReach(box.Requester);
        }

        /// Host: whether a client's player could have opened this padlock:
        /// here, in reach, by entering a code, and the right one. An
        /// unlocked padlock passes with any code.
        public static Verdict MayUnlockPadlock(in PadlockFacts pad)
        {
            if (!pad.Exists)
            {
                return Verdict.Refuse(Refusal.NoSuchPadlock);
            }

            var reach = InReach(pad.Requester);
            if (reach.Refused)
            {
                return reach;
            }

            if (!pad.Manual)
            {
                return Verdict.Refuse(Refusal.StoryOnly);
            }

            if (pad.Locked && pad.Code != pad.Combination)
            {
                return Verdict.Refuse(Refusal.WrongCode);
            }

            return Verdict.Allow;
        }

        /// Host: whether a client's player could have picked this item's
        /// lock: a crate, in reach. Only for an Item that is here and has
        /// no Trigger; a trap's disarm is TrapRules.MayDisarm.
        public static Verdict MayPick(in CrateFacts item)
        {
            if (!item.Crate)
            {
                return Verdict.Refuse(Refusal.NoLockToPick);
            }

            return InReach(item.Requester);
        }

        /// Whether an Item is a crate whose lock is picked with the
        /// progress bar (WorldActions.Crate): it takes time to disarm
        /// (Item.disarmTime above 0), has an Inventory and has no Trigger.
        public static bool IsCrate(float time, bool inventory, bool trigger)
        {
            return time > 0f && inventory && !trigger;
        }

        /// Host: how its roll for a client's pick of a crate goes. The roll
        /// is Random.Range(0f, 1f), made by the shell; a searched crate
        /// counts as picked whatever the roll. Strictly more than the roll.
        /// The shell draws the roll only for a crate that isn't searched
        /// and passes 0 otherwise, as the old `was || ...` never rolled
        /// for one, so Unity's random numbers come out in the same order.
        public static PickOutcome Pick(bool searched, float chance, float roll)
        {
            if (searched)
            {
                return PickOutcome.AlreadyOpen;
            }

            if (chance > roll)
            {
                return PickOutcome.Opened;
            }

            return PickOutcome.Failed;
        }

        /// What a game sends when one of its Locked opened: only when it
        /// was locked and isn't now, at the outermost call (so a key's or
        /// a lockpick's try sends one, with its How, and the unlock it
        /// calls none), while sharing. A door's lock goes as DoorUnlock,
        /// any other as ItemUnlock.
        public static UnlockMessage SendsUnlock(in UnlockSentFacts lck)
        {
            if (!lck.Outermost || !lck.Sharing)
            {
                return UnlockMessage.Nothing;
            }

            if (!lck.WasLocked || lck.Locked)
            {
                return UnlockMessage.Nothing;
            }

            if (lck.HasDoor)
            {
                return UnlockMessage.DoorUnlock;
            }

            return UnlockMessage.ItemUnlock;
        }

        /// Client: whether, before the unlock it sends, it flushes
        /// FlagsSync and sends the key: with a key, the host checks the
        /// key is in its journal, so the news of a key just picked up goes
        /// first. Only asked when SendsUnlock sends something.
        public static bool SendsKeyFirst(in UnlockSentFacts lck)
        {
            return lck.How == ByKey && lck.Client;
        }

        /// Whether a game sends PadlockUnlock for one of its padlocks: it
        /// was locked and isn't now, at the outermost call, while sharing.
        public static bool SendsPadlock(in PadlockSentFacts pad)
        {
            if (!pad.Outermost || !pad.Sharing)
            {
                return false;
            }

            return pad.WasLocked && !pad.Locked;
        }

        /// The code PadlockUnlock carries: the one entered when a player
        /// opened it, nothing when the story did.
        public static string PadlockCode(in PadlockSentFacts pad)
        {
            if (pad.Manually)
            {
                return pad.Code;
            }

            return "";
        }

        /// Host: whether a crate it picked open (Item.disarm) goes to the
        /// others as ItemSearched: it became searched, is a crate, no
        /// patched world call is running, while sharing. A client's picks
        /// are TrapDisarm requests instead, and the host sends those.
        public static bool SendsCrateSearched(in SearchSentFacts item)
        {
            if (item.WasSearched || !item.Searched || !item.Crate)
            {
                return false;
            }

            return item.Depth == 0 && item.Sharing && item.Host;
        }

        /// Whether a container opened the game's way (Item.openInventory)
        /// goes as ItemSearched: it became searched, while sharing. Either
        /// side sends it; WorldActions.Send filters a client's.
        public static bool SendsSearched(in SearchSentFacts item)
        {
            return !item.WasSearched && item.Searched && item.Sharing;
        }

        static Verdict InReach(in ReachFacts requester)
        {
            return ReachRules.InReach(
                requester with { Reach = ReachRules.Reach });
        }
    }
}
