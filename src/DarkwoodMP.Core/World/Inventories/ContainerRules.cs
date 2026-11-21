namespace DarkwoodMP.World.Inventories
{
    /// The decisions about whole containers (the plugin's Containers): which
    /// way an open one is shared, whether a client may change one, and what
    /// to do with contents that came from another game.
    public static class ContainerRules
    {
        /// Seconds between two looks at the open containers, on the
        /// unscaled clock (and a look at once when one opens).
        public const float PollEvery = 0.25f;

        /// Whether a container is one the mod shares: an item container, a
        /// death drop or a trader's stock, but not a workbench's own
        /// inventory (its recipes). Containers.IsWorldContainer.
        public static bool IsWorldContainer(ContainerKind kind, bool workbench)
        {
            if (kind == ContainerKind.Other)
            {
                return false;
            }

            return !workbench;
        }

        /// Which way a container the local player has open is shared
        /// (Containers.Poll): a workbench's storage slot by slot, with no
        /// lock, in every game; on a client every other one slot by slot,
        /// one player at a time; on the host its whole contents, one player
        /// at a time.
        public static ContainerPath PathOf(in OpenContainerFacts open)
        {
            if (!IsWorldContainer(open.Kind, open.Workbench) || open.OursAlone)
            {
                return ContainerPath.Ignore;
            }

            if (open.Storage)
            {
                return ContainerPath.Slots;
            }

            if (open.IsClient)
            {
                return ContainerPath.LockedSlots;
            }

            return ContainerPath.Whole;
        }

        /// Why nobody may take from or put into a container in the host's
        /// world (Containers.LockedReason): a crate not yet picked open, a
        /// lock that is locked, or one that has to be broken open first.
        /// The first that applies wins.
        public static Verdict LockedReason(in LockFacts shut)
        {
            if (shut.Crate && !shut.Searched)
            {
                return Verdict.Refuse(Refusal.Locked);
            }

            if (shut.Locked)
            {
                return Verdict.Refuse(Refusal.Locked);
            }

            if (shut.DestroyToAccess && !shut.Destroyed)
            {
                return Verdict.Refuse(Refusal.NotBrokenOpen);
            }

            return Verdict.Allow;
        }

        /// Host: whether a client may change a container
        /// (Containers.Refusal). Too far wins over locked. A client with no
        /// stand-in yet isn't measured. Only the stand-in counts, not the
        /// position the movement check accepted (Authority.InReach takes
        /// both), so this has its own words.
        public static Verdict MayChange(in ContainerFacts container)
        {
            if (container.Known)
            {
                var away = Vec2.Distance(container.StandIn, container.At);
                if (away > ReachRules.Reach)
                {
                    return Verdict.Refuse(Refusal.TooFar);
                }
            }

            return LockedReason(container.Lock);
        }

        /// Whether this game takes a message about the world from a peer:
        /// only while in a world, and on a client only from the host.
        public static bool Accepts(bool inWorld, bool isClient, bool fromHost)
        {
            if (!inWorld)
            {
                return false;
            }

            return !isClient || fromHost;
        }

        /// What to do with a container's whole contents from another game
        /// (Containers.OnContainer). The host refuses a client's for a
        /// container it has when MayChange refuses it; a container the host
        /// hasn't got is passed on unchecked. Otherwise the contents load
        /// here if the container is here, and the host passes them on.
        public static ContentsAnswer Received(
            bool isHost,
            bool found,
            Verdict change)
        {
            if (isHost && found && change.Refused)
            {
                return new ContentsAnswer { Verdict = change };
            }

            return new ContentsAnswer { Apply = found, Relay = isHost };
        }

        /// How contents from another game load into a container
        /// (Containers.Apply): into its open window when it has the same
        /// number of slots and windows redraw (UiRefresh.Enabled); an open
        /// window with another number is closed first.
        public static LoadPath LoadPathOf(
            bool open,
            bool sameSlots,
            bool redraws)
        {
            if (open && sameSlots && redraws)
            {
                return LoadPath.InWindow;
            }

            if (open)
            {
                return LoadPath.CloseFirst;
            }

            return LoadPath.Load;
        }

        /// Whether contents loaded into an open window are merged slot by
        /// slot (SlotShare.MergeOpen) instead of loaded whole: on a client
        /// that watches the container slot by slot (Containers.ApplyOpen).
        public static bool MergesInWindow(bool isClient, bool watching)
        {
            return isClient && watching;
        }

        /// Whether a container goes once contents were loaded into it
        /// (Containers.Apply): one that goes when emptied
        /// (Inventory.removeWhenEmpty), now empty. A dropped item isn't
        /// counted here, unlike SlotRules.Removes.
        public static bool RemovesAfterLoad(bool removeWhenEmpty, int items)
        {
            return removeWhenEmpty && items == 0;
        }
    }
}
