using DarkwoodMP.Menus;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Workbenches;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// SlotShare: the slot changes sent and taken (ContainerSlots), and the
    /// host's refusals of them (ContainerSlotRefused).
    /// </summary>
    internal sealed partial class SlotShare
    {
        // ---- messages -------------------------------------------------------

        // Client: the batches of slot changes we sent lately, to undo one whole
        // when the host refuses it.
        private readonly SentBatches _sent = new SentBatches();

        private void RememberSent(ObjRef obj, List<SlotChange> changes)
        {
            _sent.Remember(
                obj.ToString(), changes, new Seconds(Time.unscaledTime));
        }

        private void OnSlots(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld)
            {
                return;
            }

            if (s.IsClient && from != s.HostId)
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerSlotsMessage.Read(r, out var changes));

            var inv = ObjRef.Resolve<Inventory>(obj);
            if (WorldActions.Debug)
            {
                string name = "not found here";
                if (inv != null)
                {
                    name = inv.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] got {changes.Count} slot(s) of {obj}: " + name);
            }

            if (inv == null)
            {
                return;
            }

            if (s.IsClient) // the host's word
            {
                foreach (var c in changes)
                {
                    TakeHost(inv, c);
                }

                _coop.Ui.Changed(inv);
                RemoveIfEmptied(inv);
                return;
            }

            // Host: the player must be at the container, it must be open in the
            // host's world (a locked crate not picked open here isn't), and
            // nobody else may have it open (but its storage). The changes
            // stand if every slot still holds what the player saw there; if
            // one doesn't, none does. A batch is what one look at the
            // container found changed: an item moved from one slot to another
            // (a cursor drag inside it) is two changes, and taking only one of
            // them would double or lose that item. A stack bigger than the
            // host's stack size (StackSizes) isn't one the client could have
            // made.
            changes.RemoveAll(c => !SlotRules.InRange(c.Slot, inv.slots.Count));
            var batch = FactsOf(from, inv, obj) with
            {
                Changes = FactsOf(inv, changes),
            };

            var verdict = SlotRules.MayChange(batch);
            string refusedAll = null;
            if (verdict.Refused)
            {
                refusedAll = Refusals.Text(verdict);
            }

            var accepted = new List<SlotChange>();
            foreach (var c in changes)
            {
                if (refusedAll == null)
                {
                    Set(inv, c.Slot, c.New);
                    accepted.Add(c);
                    continue;
                }

                Plugin.Log.LogInfo(
                    $"[authority] {s.NameOf(from)} changed slot {c.Slot} of "
                        + $"{inv.name}: refused ({refusedAll})");
                var w = s.Begin(MsgType.ContainerSlotRefused);
                ContainerSlotRefusedMessage.Write(
                    w,
                    ObjRef.NameOf(obj),
                    new RefusedSlot
                    {
                        Slot = c.Slot,
                        Held = Json(inv, c.Slot),
                        Old = c.Old,
                        Tried = c.New,
                    });

                s.SendTo(from, w, Channel.Control, reliable: true);
            }

            if (accepted.Count == 0)
            {
                return;
            }

            _coop.Ui.Changed(inv);
            SendChanges(obj, accepted, from);
            RemoveIfEmptied(inv);
        }

        /// <summary>
        /// What the host looks at in a container a client changed; Changes is
        /// left for the caller.
        /// </summary>
        private SlotBatchFacts FactsOf(ulong from, Inventory inv, ObjRef obj)
        {
            string holder = null;
            CoopSession coop = Coop.Current;
            bool held = coop != null
                && coop.Containers.LockedByOther(obj, from, out holder);
            return new SlotBatchFacts
            {
                Container = Containers.MayChange(from, inv),
                Storage = Is(inv),
                HeldByOther = held,
                Holder = holder,
                SlotCount = inv.slots.Count,
            };
        }

        /// <summary>
        /// What the host looks at in each change of a client's batch (all
        /// within the container's slots).
        /// </summary>
        private static List<SlotChangeFacts> FactsOf(
            Inventory inv, List<SlotChange> changes)
        {
            var facts = new List<SlotChangeFacts>(changes.Count);
            foreach (var c in changes)
            {
                var here = inv.slots[c.Slot].invItem;
                facts.Add(new SlotChangeFacts
                {
                    Slot = c.Slot,
                    Old = c.Old,
                    HostKey = Key(here),
                    Host = FactsOf(here),
                    Fits = StackSizes.Fits(Item(c.New)),
                });
            }

            return facts;
        }

        /// <summary>
        /// Client: another player's change to a slot, as the host took it. A
        /// change of ours to that slot not yet sent stands if the slot now
        /// holds what we changed it from (it goes at the next Check); otherwise
        /// the host would refuse it, so it's undone now, as Merge does, and Set
        /// doesn't double or lose what it moved.
        /// </summary>
        private void TakeHost(Inventory inv, SlotChange c)
        {
            var slot = FactsOf(inv, c.Slot, Watched(inv)) with
            {
                HostKey = Key(Item(c.New)),
            };

            var step = SlotRules.TakeHost(slot);
            if (step == SlotStep.Take)
            {
                Set(inv, c.Slot, c.New);
                return;
            }

            if (step != SlotStep.TakeAndUndo)
            {
                return;
            }

            string oldKey = slot.SeenKey;
            var tried = Item(Json(inv, c.Slot));
            Set(inv, c.Slot, c.New);
            Plugin.Log.LogInfo(
                $"[authority] our change to slot {c.Slot} of {inv.name}: "
                    + "refused here (another player changed it first)");
            Undo(oldKey, tried);
        }

        /// <summary>
        /// Client: the host refused a slot change; take the host's slot, and
        /// undo what the change moved.
        /// </summary>
        private void OnRefused(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsClient || from != s.HostId || !WorldShare.InWorld)
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerSlotRefusedMessage.Read(r, out var refused));
            int slot = refused.Slot;
            string hostJson = refused.Held,
                oldKey = refused.Old,
                triedJson = refused.Tried;
            // may be gone here: a dropped item we picked up with a click
            var inv = ObjRef.Resolve<Inventory>(obj);
            Plugin.Log.LogInfo(
                "[slots] "
                    + $"{obj} slot {slot}: the host refused our change "
                    + $"'{oldKey}' -> '{Key(Item(triedJson))}' "
                    + $"(it holds '{Key(Item(hostJson))}' there)");
            // Something we changed in that slot after this change and haven't
            // sent yet: Set replaces it too, so it's undone as well. (A later
            // change already sent is refused, and undone, by its own answer.)
            InvItemClass unsent = null;
            string sentKey = null;
            if (inv != null)
            {
                var here = FactsOf(inv, slot, Watched(inv));
                if (SlotRules.HasUnsent(here))
                {
                    sentKey = here.SeenKey;
                    unsent = Item(Json(inv, slot));
                }
            }

            if (inv != null)
            {
                Set(inv, slot, hostJson);
            }

            // What this change moved: from what we saw (its key) to what we
            // made it. The host refuses a batch whole, so the first refused
            // slot of a batch we sent undoes the whole batch, netted per item
            // type (an item we moved within the container comes to nothing);
            // its other slots' refusals only set the host's contents.
            var batch = _sent.Find(
                obj.ToString(),
                new SlotChange
                {
                    Slot = slot,
                    Old = oldKey,
                    New = triedJson,
                });

            bool undone = false;
            if (batch != null)
            {
                undone = batch.Undone;
            }

            var undo = SlotRules.UndoOnRefusal(batch != null, undone);
            if (undo == RefusalUndo.Alone)
            {
                Undo(oldKey, Item(triedJson));
            }
            else if (undo == RefusalUndo.Batch)
            {
                batch.Undone = true;
                var moves = new Moves();
                foreach (var c in batch.Changes)
                {
                    moves.Add(c.Old, Item(c.New));
                }

                Undo(moves, quiet: false);
            }

            if (sentKey != null)
            {
                Undo(sentKey, unsent, quiet: true);
            }

            _coop.Ui.Changed(inv);
        }
    }
}
