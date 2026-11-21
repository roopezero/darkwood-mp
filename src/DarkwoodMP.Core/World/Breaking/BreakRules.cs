using DarkwoodMP.World.Eggs;

namespace DarkwoodMP.World.Breaking
{
    /// Hits on items, breaking and repairing them, and explosives going off
    /// (the plugin's BreakSync): whose change each one is, and what is
    /// sent. The host's checks of these messages are WorldActions'.
    public static class BreakRules
    {
        /// Client: whether the host makes this hit, not this game: one
        /// inside a copy of a thrown thing's explosion, or from a flame
        /// that isn't the local player's.
        public static bool HostsHit(in HitFacts hit)
        {
            if (!hit.ClientMode)
            {
                return false;
            }

            if (hit.ThrownDepth > 0)
            {
                return true;
            }

            return hit.Flame && !hit.FlameOurs;
        }

        /// Client: whether the damage now is the local player's own flame,
        /// outside any explosion.
        public static bool OwnFlame(in HitFacts hit)
        {
            if (!hit.ClientMode || hit.ThrownDepth != 0)
            {
                return false;
            }

            return hit.Flame && hit.FlameOurs;
        }

        /// Whose change this is (BreakSync.OurPlayers). The host's, or a
        /// game played alone, is always its own. A client's inside an
        /// explosion is the host's; its own flame, or a hit whose attacker
        /// is its player, its own. Anything else the stack says.
        public static HitOwner OwnerOf(in HitFacts hit)
        {
            if (!hit.ClientMode)
            {
                return HitOwner.Ours;
            }

            if (hit.ThrownDepth > 0)
            {
                return HitOwner.Others;
            }

            if (OwnFlame(hit) || hit.AttackerIsPlayer)
            {
                return HitOwner.Ours;
            }

            return HitOwner.AskStack;
        }

        /// Whether an item's change counts at all: the outermost patched
        /// call, while sharing, on an item still there. Then our player's
        /// hit moves what it hits (DragSync.Hit), even when nothing about
        /// the item changed.
        public static bool Counts(bool outermost, bool sharing, bool exists)
        {
            return outermost && sharing && exists;
        }

        /// What our game sends after a hit that counts. Nothing when the
        /// item didn't change or it wasn't our player's; an egg the hit
        /// broke goes as EggDestroyed.
        public static ItemSend HitSend(in ItemHitFacts hit)
        {
            if (hit.Before.Equals(hit.After) || !hit.Ours)
            {
                return ItemSend.Nothing;
            }

            if (!hit.Before.Destroyed && hit.After.Destroyed)
            {
                if (EggRules.IsEgg(
                    hit.Destructible,
                    hit.OpensByBreaking,
                    hit.Name))
                {
                    return ItemSend.EggDestroyed;
                }
            }

            return ItemSend.ItemHit;
        }

        /// Whether an Item.die broke the item in a way to send, before the
        /// stack says whose it is (OwnerOf with no attacker).
        public static bool Broke(in ItemChangeFacts item)
        {
            if (!item.Outermost || !item.Sharing || !item.Exists)
            {
                return false;
            }

            return !item.WasDestroyed && item.Destroyed;
        }

        /// What our player's break is sent as: EggDestroyed for an egg,
        /// else ItemBroken.
        public static ItemSend BrokenAs(
            bool destructible,
            bool opensByBreaking,
            string name)
        {
            if (EggRules.IsEgg(destructible, opensByBreaking, name))
            {
                return ItemSend.EggDestroyed;
            }

            return ItemSend.ItemBroken;
        }

        /// Whether an Item.setNormalState repaired the item in a way to
        /// send, before the stack says whose it is.
        public static bool Repaired(in ItemChangeFacts item)
        {
            if (!item.Outermost || !item.Sharing || !item.Exists)
            {
                return false;
            }

            return item.WasDestroyed && !item.Destroyed;
        }

        /// Whether an explosive going off is tracked and sent. Not one gone
        /// off already, a copy, a thrown one (ThrowSync's), a kamikaze or
        /// what an enemy threw: enemy explosions happen only on the host.
        public static bool Tracks(in ExplodeFacts bomb)
        {
            if (!bomb.Exists || bomb.Activated || bomb.Proxy)
            {
                return false;
            }

            if (bomb.Thrown)
            {
                return false;
            }

            return !bomb.Character && !bomb.ByEnemy;
        }

        /// The guards while a tracked explosive goes off. On the host the
        /// hits it makes count as outermost and not replayed; elsewhere it
        /// is one call deeper.
        public static Guards Enter(bool host, Guards now)
        {
            if (host)
            {
                return new Guards { Depth = 0, Replaying = false };
            }

            return new Guards
            {
                Depth = now.Depth + 1,
                Replaying = now.Replaying,
            };
        }

        /// The guards after it, before WorldActions.Outermost: on the host,
        /// the saved ones with the depth one deeper, so that Outermost
        /// takes it back; elsewhere as they are now.
        public static Guards Leave(bool host, Guards saved, Guards now)
        {
            if (host)
            {
                return new Guards
                {
                    Depth = saved.Depth + 1,
                    Replaying = saved.Replaying,
                };
            }

            return now;
        }

        /// Whose explosion a tracked one is, once it went off. Nothing to
        /// send unless outermost, sharing and gone off; on a client, only
        /// our player's, which the stack says.
        public static HitOwner ExplodeOwner(in ExplodeSendFacts bomb)
        {
            if (!bomb.Outermost || !bomb.Sharing || !bomb.Activated)
            {
                return HitOwner.Others;
            }

            if (bomb.ClientMode)
            {
                return HitOwner.AskStack;
            }

            return HitOwner.Ours;
        }
    }
}
