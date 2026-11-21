using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Workbenches;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// Containers changed slot by slot, with the host deciding: the workbench's
    /// storage, used by several players at once, and every container a client
    /// changes (the host's own changes go out whole: Containers). The host
    /// settles who got there first, and refuses a change from a player who
    /// isn't at the container, to one that is still locked in the host's world
    /// (a crate not picked open there: Containers.LockedReason), or while
    /// someone else has it open.
    ///
    /// While a player has the storage open, its slots are compared a few times
    /// a second with how they were.
    /// Each slot that changed goes out as ContainerSlots:
    /// "slot i was A, is now B". The host makes the change in its world only if
    /// slot i is still A there, and passes it on; the host's own changes go
    /// straight out. If slot i had already changed (two players took the same
    /// stack at the same moment), the host refuses (ContainerSlotRefused, with
    /// what slot i holds): that player's slot is set to the host's, and what
    /// their change moved is undone for them. Items they put in go back into
    /// their bags or hotbar (at their feet only if there's no room); items they
    /// took are taken back from what they carry. Nothing is lost or doubled.
    ///
    /// A client's "slot i was A" must be what the host has, or every change is
    /// refused: its copy of the world is the host's save from when it joined,
    /// and changes the host made while it downloaded and loaded it never
    /// reached it. So a client asks the host what a container holds as it opens
    /// it (ContainerSlotsAsk), and sends no change before the answer
    /// (ContainerSlotsAll) has become its starting point; and asks for every
    /// workbench's storage once its world has loaded.
    ///
    /// A slot is compared by its item's type, amount, durability, recipe and
    /// upgrades; its contents travel as the game saves them
    /// (InvItemClass.SaveState, as JSON; "" for an empty slot).
    ///
    /// Food rots, and some items drain or recharge, by themselves while their
    /// container is on the game's refreshing list (Inventory.refreshItems),
    /// which each game runs on its own. Those durability changes are the
    /// world's, not the player's: they become the slot's starting point without
    /// being sent (Drift). The host accepts a change to a slot whose only
    /// difference from what the client saw is such an item's durability
    /// (RotOnly), and starts the rot in its own copy when a client opens the
    /// container (OnAsk), as drawing the slots would. A client takes the host's
    /// durability each time it opens the container.
    ///
    /// The workbench's level is shared too (WorkbenchLevel): an upgrade made at
    /// anyone's workbench raises it for everyone. The highest level wins.
    /// </summary>
    internal sealed partial class SlotShare
    {
        private readonly Session _session;

        // what each watched storage's slots held when last compared (their
        // keys)
        private readonly Dictionary<Inventory, string[]> _watched =
            new Dictionary<Inventory, string[]>();
        private readonly Dictionary<Inventory, ObjRef> _refs =
            new Dictionary<Inventory, ObjRef>();
        private readonly Dictionary<Inventory, bool> _isStorage =
            new Dictionary<Inventory, bool>();

        public SlotShare(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.ContainerSlots, OnSlots);
            coop.Handle(MsgType.ContainerSlotRefused, OnRefused);
            coop.Handle(MsgType.WorkbenchLevel, OnLevel);
            coop.Handle(MsgType.ContainerSlotsAsk, OnAsk);
            coop.Handle(MsgType.ContainerSlotsAll, OnContents);
            coop.Handle(MsgType.StorageSort, OnSort);
        }

        /// <summary>
        /// Whether this container is a workbench's storage (shared slot by
        /// slot).
        /// </summary>
        public bool Is(Inventory inv)
        {
            if (inv == null)
            {
                return false;
            }

            if (_isStorage.TryGetValue(inv, out bool known))
            {
                return known;
            }

            bool yes = false;
            foreach (var wb in Object.FindObjectsOfType<Workbench>())
            {
                if (wb != null && wb.normalInventory == inv)
                {
                    yes = true;
                    break;
                }
            }

            _isStorage[inv] = yes;
            return yes;
        }

        // ---- watching -------------------------------------------------------

        /// <summary>
        /// Called a few times a second with each storage the local player has
        /// open. The first call records the slots as they are here; on a client
        /// it also asks the host what they hold there (ContainerSlotsAsk), and
        /// no change is sent until that answer has become the starting point
        /// (OnContents), or AskTimeout went by. A click pickup (askHost false)
        /// is over within one frame, so it doesn't wait.
        /// </summary>
        public void Check(Inventory inv, bool askHost = true)
        {
            if (inv == null)
            {
                return;
            }

            var keys = Keys(inv);
            _watched.TryGetValue(inv, out var before);
            if (SlotRules.IsFirstLook(before, keys.Length))
            {
                _watched[inv] = keys; // as we found it
                var found = ObjRef.Of(inv.gameObject);
                _refs[inv] = found;
                if (SlotRules.Asks(askHost, _session.IsClient, found.Valid))
                {
                    Ask(inv, found);
                }

                return;
            }

            if (_awaiting.TryGetValue(inv, out float asked))
            {
                var now = new Seconds(Time.unscaledTime);
                if (SlotRules.StillWaiting(new Seconds(asked), now))
                {
                    // what we change meanwhile goes once the host's answer is
                    // in
                    return;
                }

                _awaiting.Remove(inv);
                Plugin.Log.LogWarning(
                    $"[slots] {_refs[inv]} no contents from host in "
                        + $"{SlotRules.AskTimeout:0} s; sending changes "
                        + "against what we saw");
            }

            var changed = new List<SlotChange>();
            foreach (int i in SlotRules.Changed(before, keys))
            {
                changed.Add(new SlotChange
                {
                    Slot = i,
                    Old = before[i],
                    New = Json(inv, i)
                });
            }

            if (changed.Count == 0)
            {
                return;
            }

            var r = _refs[inv];
            if (!r.Valid)
            {
                return;
            }

            SendChanges(r, changed);
            if (_session.IsClient)
            {
                RememberSent(r, changed);
            }

            if (WorldActions.Debug)
            {
                string changes = string.Join(
                    ", ",
                    changed.ConvertAll(
                        c => $"{c.Slot}: '{c.Old}' -> '{keys[c.Slot]}'"));
                Plugin.Log.LogInfo(
                    $"[world] sent {changed.Count} slot(s) of {inv.name} "
                        + $"{r}: {changes}");
            }

            _watched[inv] = keys;
        }

        /// <summary>
        /// The local player closed it: one last look, then stop watching.
        /// </summary>
        public void Closed(Inventory inv)
        {
            // closed before the host answered: what we changed goes against
            // what we saw
            _awaiting.Remove(inv);
            if (inv != null)
            {
                Check(inv);
            }
            // destroyed (a Unity object that is == null but still a key here):
            // its slots went empty
            else if (_watched.TryGetValue(inv, out var before)
                && _refs.TryGetValue(inv, out var r) && r.Valid)
            {
                SendEmptied(r, before);
            }

            _watched.Remove(inv);
            _refs.Remove(inv);
        }

        // ---- click pickups --------------------------------------------------

        /// <summary>
        /// A dropped item is about to be picked up with a click
        /// (GetDroppedItem), which moves its slot to the player and destroys
        /// the object without opening its window, so Containers never sees
        /// it. Its slot is looked at before and after, as if the window had
        /// been open for that moment: the change goes out slot by slot (a
        /// client's is the host's to accept), and the others' copy goes once
        /// it's empty. Returns the container to look at after, or null.
        /// </summary>
        internal Inventory PickingUp(Inventory inv)
        {
            if (inv == null || !Coop.Guards.Sharing || Watching(inv))
            {
                return null;
            }

            if (WorldActions.OursAlone(inv.gameObject))
            {
                return null; // the host hasn't got it (Containers.Poll)
            }

            // as it is now (over within this frame: no waiting for the host)
            Check(inv, askHost: false);
            return inv;
        }

        /// <summary>
        /// The click pickup PickingUp looked at is done.
        /// </summary>
        internal void PickedUp(Inventory inv)
        {
            if (inv == null)
            {
                return;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] picked up {inv.name} with a click");
            }

            Closed(inv);
        }
    }
}
