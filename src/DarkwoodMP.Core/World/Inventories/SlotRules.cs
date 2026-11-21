using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// The decisions about containers shared slot by slot (the plugin's
    /// SlotShare): what a look at a container sends, when a client waits
    /// for the host, whether the host takes a client's batch, and how a
    /// client takes the host's slots.
    public static class SlotRules
    {
        /// Seconds a client waits for the host's contents of something it
        /// opened before it sends its changes anyway, on the unscaled clock.
        public const float AskTimeout = 3f;

        /// Seconds a client remembers a batch it sent, to undo it whole if
        /// the host refuses it, on the unscaled clock.
        public const float SentKept = 15f;

        /// Whether a look at a container is the first (SlotShare.Check): we
        /// have no keys for it, or as many as it has slots no more. The
        /// slots are then recorded as found, and nothing is sent.
        public static bool IsFirstLook(string[] before, int slots)
        {
            return before == null || before.Length != slots;
        }

        /// Whether a client asks the host for the contents at a first look:
        /// not for a click pickup (over within a frame), only on a client,
        /// and only for a container with a name in the host's world.
        public static bool Asks(bool askHost, bool isClient, bool named)
        {
            return askHost && isClient && named;
        }

        /// Whether a client still waits for the host's contents it asked
        /// for, sending nothing meanwhile.
        public static bool StillWaiting(Seconds asked, Seconds now)
        {
            return (now - asked).Value < AskTimeout;
        }

        /// The slots whose keys differ between two looks of the same size,
        /// in slot order.
        public static List<int> Changed(string[] before, string[] now)
        {
            var slots = new List<int>();
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] != before[i])
                {
                    slots.Add(i);
                }
            }

            return slots;
        }

        /// The slots a container that went held at the last look: each
        /// goes empty (SlotShare.SendEmptied).
        public static List<int> Emptied(string[] before)
        {
            var slots = new List<int>();
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i].Length > 0)
                {
                    slots.Add(i);
                }
            }

            return slots;
        }

        /// The keys of our last look after the world's own refresh (rot,
        /// drain, recharge) changed a container from before to now: a slot
        /// that changed in it, and that our player hadn't changed since the
        /// last look, takes its new key. Returns a new array.
        public static string[] Drift(
            string[] keys,
            string[] before,
            string[] now)
        {
            var kept = (string[])keys.Clone();
            for (int i = 0; i < kept.Length && i < before.Length; i++)
            {
                if (i >= now.Length)
                {
                    break;
                }

                if (now[i] != before[i] && kept[i] == before[i])
                {
                    kept[i] = now[i];
                }
            }

            return kept;
        }

        /// The keys of our last look once the container got more slots
        /// (SlotShare.Grew): the new ones as they are now. Unchanged when
        /// it didn't grow.
        public static string[] Grown(string[] keys, string[] now)
        {
            if (keys.Length >= now.Length)
            {
                return keys;
            }

            var grown = new string[now.Length];
            for (int i = 0; i < grown.Length; i++)
            {
                if (i < keys.Length)
                {
                    grown[i] = keys[i];
                }
                else
                {
                    grown[i] = now[i];
                }
            }

            return grown;
        }

        /// Whether the host keeps a change to this slot: changes to slots
        /// past the container's count are dropped, silently.
        public static bool InRange(int slot, int slots)
        {
            return slot < slots;
        }

        /// Host: whether a client's batch stands (SlotShare.OnSlots). One
        /// reason refuses all of it, the first of: the container's own
        /// (too far, locked); another player has it open (not for a
        /// storage); a slot that had changed since the client looked (the
        /// first in order), named only when the batch has more than one
        /// change; a slot that would hold more than one stack. Changes past
        /// the slot count are left out of the checks and of the count.
        public static Verdict MayChange(in SlotBatchFacts batch)
        {
            if (batch.Container.Refused)
            {
                return batch.Container;
            }

            if (!batch.Storage && batch.HeldByOther)
            {
                return Verdict.Refuse(Refusal.HasItOpen, batch.Holder);
            }

            int count = 0;
            foreach (var change in batch.Changes)
            {
                if (InRange(change.Slot, batch.SlotCount))
                {
                    count++;
                }
            }

            foreach (var change in batch.Changes)
            {
                if (!InRange(change.Slot, batch.SlotCount) || Holds(change))
                {
                    continue;
                }

                if (count > 1)
                {
                    return Verdict.Refuse(
                        Refusal.SlotChanged,
                        Refusals.Detail(
                            change.Slot.ToString(),
                            count.ToString()));
                }

                return Verdict.Refuse(Refusal.JustChanged);
            }

            foreach (var change in batch.Changes)
            {
                if (InRange(change.Slot, batch.SlotCount) && !change.Fits)
                {
                    return Verdict.Refuse(
                        Refusal.OverStack,
                        change.Slot.ToString());
                }
            }

            return Verdict.Allow;
        }

        /// Client: another player's change to a slot, as the host took it
        /// (SlotShare.TakeHost). A slot we haven't changed since our last
        /// look takes it; one we changed from what the host's change has
        /// too (or by rot only) keeps ours, which goes at the next look;
        /// otherwise ours is undone.
        public static SlotStep TakeHost(in SlotFacts slot)
        {
            if (!slot.Seen || slot.SeenKey == slot.NowKey)
            {
                return SlotStep.Take;
            }

            return Settle(slot);
        }

        /// Client: the host's whole contents of a container we have open,
        /// one slot (SlotShare.Merge). As TakeHost, but an unchanged slot
        /// that holds what the host's does is left as it is.
        public static SlotStep Merge(in SlotFacts slot)
        {
            if (!slot.Seen || slot.NowKey == slot.SeenKey)
            {
                if (slot.NowKey != slot.HostKey)
                {
                    return SlotStep.Take;
                }

                return SlotStep.None;
            }

            return Settle(slot);
        }

        /// Client: whether the slot holds a change of ours not yet sent,
        /// which the host's refusal of an earlier one replaces too, so it's
        /// undone quietly (SlotShare.OnRefused).
        public static bool HasUnsent(in SlotFacts slot)
        {
            return slot.Seen && slot.SeenKey != slot.NowKey;
        }

        /// Client: what a refusal of one of our changes undoes. The first
        /// refusal of a batch we remember undoes the batch; the others only
        /// set the host's slot.
        public static RefusalUndo UndoOnRefusal(bool inBatch, bool undone)
        {
            if (!inBatch)
            {
                return RefusalUndo.Alone;
            }

            if (!undone)
            {
                return RefusalUndo.Batch;
            }

            return RefusalUndo.None;
        }

        /// Client: what to do with the host's answer to our ask
        /// (SlotShare.OnContents). Unwatched and closed, it loads whole;
        /// otherwise slot by slot. An answer to an ask we stopped waiting
        /// for, while we watch it, comes after our changes and isn't used.
        public static HostContents Answer(in AnswerFacts answer)
        {
            if (!answer.Found)
            {
                return HostContents.NotFound;
            }

            if (answer.Empty)
            {
                return HostContents.HostHasNone;
            }

            if (answer.Watched && !answer.Waited)
            {
                return HostContents.NotUsed;
            }

            if (!answer.Watched && !answer.Open)
            {
                return HostContents.LoadWhole;
            }

            return HostContents.Merge;
        }

        /// Whether a storage takes the host's rows first: the host's has
        /// more slots (WorkbenchStorage.Fit).
        public static bool GrowsRows(int hostSlots, int slots, bool storage)
        {
            return hostSlots > slots && storage;
        }

        /// Whether an emptied container goes (SlotShare.RemoveIfEmptied):
        /// one that goes when emptied (Inventory.removeWhenEmpty) or a
        /// dropped item (Item.isDroppedItem), once it holds nothing.
        public static bool Removes(
            int items,
            bool removeWhenEmpty,
            bool dropped)
        {
            if (items > 0)
            {
                return false;
            }

            return removeWhenEmpty || dropped;
        }

        /// Whether the item our player holds goes into our bags before a
        /// slot is set (SlotShare.Set): it came from that slot, which
        /// another game fills now, and the game would put it back over
        /// theirs when let go.
        public static bool HeldGoesToBags(in HeldFacts held)
        {
            if (!held.Filling || !held.Holding)
            {
                return false;
            }

            return held.FromThisSlot && held.PlayerHere;
        }

        /// The amount to create a slot's item with (SlotShare.Set): a gun's
        /// saved amount is its loaded rounds, so it's created with -1, which
        /// keeps the amount of 1 its copy has, not 0; any other item with
        /// its amount.
        public static int CreateAmount(bool hasAmmo, int amount)
        {
            if (hasAmmo)
            {
                return -1;
            }

            return amount;
        }

        /// The host's slot still holds what the client saw, or differs
        /// from it by rot only.
        static bool Holds(in SlotChangeFacts change)
        {
            if (change.HostKey == change.Old)
            {
                return true;
            }

            return SlotKeyRules.RotOnly(
                change.Host,
                change.HostKey,
                change.Old);
        }

        /// A slot we changed: keep it when the host has what we saw (or
        /// differs by rot only), else take the host's and undo ours.
        static SlotStep Settle(in SlotFacts slot)
        {
            if (slot.HostKey == slot.SeenKey)
            {
                return SlotStep.Keep;
            }

            if (SlotKeyRules.RotOnly(slot.Here, slot.HostKey, slot.SeenKey))
            {
                return SlotStep.Keep;
            }

            return SlotStep.TakeAndUndo;
        }
    }
}
