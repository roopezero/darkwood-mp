using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for containers and items picked up.</summary>
    public static partial class MapScenarioWorld
    {
        // ---- containers -----------------------------------------------------

        internal static Inventory Container(string spec) =>
            Find<Inventory>(
                spec,
                i => i.invType == Inventory.InvType.itemInv && !i.isWorkbench
                    && i != P.Inventory && i != P.Hotbar
                    && !i.transform.IsChildOf(P.transform));

        /// <summary>
        /// "put-item CONTAINER [TYPE] [AMOUNT]": put something we carry (of
        /// TYPE) into it, as if its window were open.
        /// </summary>
        private static IEnumerator PutItem(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }

            var item =
                Carried().FirstOrDefault(i => a.Length < 2 || i.type == a[1]);
            if (item == null)
            {
                string wantedType = "anything";
                if (a.Length > 1)
                {
                    wantedType = a[1];
                }

                r.Fail($"not carrying {wantedType}");
                yield break;
            }

            string type = item.type;
            int asked = 1;
            if (a.Length > 2)
            {
                asked = int.Parse(a[2]);
            }

            int amount = Math.Min(asked, item.amount);
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            // a client asks what it holds first
            yield return new WaitForSeconds(0.8f);
            int before =
                inv.getAllItems().Where(i => i.type == type).Sum(i => i.amount);
            AsPlayer(() =>
            {
                P.Inventory.removeItemAmountFromPlayer(type, amount);
                inv.addItemType(type, amount);
            });

            yield return new WaitForSeconds(1f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(1f);
            int after =
                inv.getAllItems().Where(i => i.type == type).Sum(i => i.amount);
            r.Info =
                $"{type} x{amount} into {inv.name} {Ref(inv)}: "
                    + $"[{Contents(inv)}]";
            if (after <= before)
            {
                r.Fail($"{r.Info}: it isn't in there (refused?)");
            }
        }

        /// <summary>"take-from CONTAINER [--refused]": take its first item.
        /// With --refused, the host should say no and the item go
        /// back.</summary>
        private static IEnumerator TakeFrom(string[] a, Result r)
        {
            bool refused = a.Contains("--refused");
            a = a.Where(x => x != "--refused").ToArray();
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }

            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(0.8f);
            var slot = inv.slots.FirstOrDefault(
                s => s != null && !InvItemClass.isNull(s.invItem));
            if (slot == null)
            {
                P.openedItemInventory = null;
                r.Fail($"{inv.name} {Ref(inv)} is empty here");
                yield break;
            }

            var item = new InvItemClass(slot.invItem);
            int before = CarriedOf(item.type);
            AsPlayer(() =>
            {
                slot.removeItem();
                P.Inventory.addItemTypeToPlayer(
                    item.type, item.amount, dropIfNoRoom: true);
            });

            yield return new WaitForSeconds(1.5f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(1f);
            int now = CarriedOf(item.type);
            r.Info =
                $"{item.type} x{item.amount} from {inv.name} {Ref(inv)}: we "
                    + $"carry {before} -> {now}; it holds [{Contents(inv)}]";
            if (refused && now > before)
            {
                r.Fail($"{r.Info}: the host let us take it");
            }

            if (!refused && now <= before)
            {
                r.Fail($"{r.Info}: not in our bags");
            }
        }

        private static Inventory _held;

        /// <summary>
        /// "hold-open CONTAINER": open it and keep it open (until "release").
        /// </summary>
        private static IEnumerator HoldOpen(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }

            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            _held = inv;
            yield return new WaitForSeconds(1f);
            r.Info = $"holding {inv.name} {Ref(inv)} open";
        }

        private static IEnumerator Release(string[] a, Result r)
        {
            if (P.openedItemInventory == _held)
            {
                P.openedItemInventory = null;
            }

            if (_held != null)
            {
                r.Info = $"closed {_held.name}";
            }
            else
            {
                r.Info = "nothing held";
            }

            _held = null;
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>
        /// "open-busy CONTAINER": open it while the other player has it: we
        /// should be told "X is using this".
        /// </summary>
        private static IEnumerator OpenBusy(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }

            yield return Walk(inv.transform.position);
            float t0 = Time.realtimeSinceStartup;
            P.openedItemInventory = inv;
            string msg = null;
            while (msg == null && Time.realtimeSinceStartup < t0 + 3f)
            {
                msg = Messages.Where(
                    m => m.at >= t0
                        && m.msg.Contains("is using this")).Select(m => m.msg)
                    .FirstOrDefault();
                yield return null;
            }

            P.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            string name = inv.name;
            string id = Ref(inv);
            string message = msg;
            if (message == null)
            {
                message = "no message";
            }

            r.Info = $"{name} {id}: {message}";
            if (msg == null)
            {
                r.Fail($"{r.Info}: we weren't told someone is using it");
            }
        }

        /// <summary>"pick-crate NAME": open a locked crate as the player does.
        /// One that's picked with the progress bar (Item.disarmTime, like the
        /// wooden loot crate): the bar's end, Player.progressBarCompleted with
        /// the crate as the trap being disarmed; the roll is the host's, so
        /// it's tried until it opens. One with a lock (Locked, like
        /// crate_locked_difficulty1): a lockpick
        /// (Locked.tryToLockpick).</summary>
        private static IEnumerator PickCrate(string[] a, Result r)
        {
            var crate = Find<Item>(
                a[0],
                i => WorldActions.Crate(i) != null && !i.searched
                    || i.GetComponent<Locked>() is Locked lk && lk.locked);
            if (crate == null)
            {
                r.Fail($"no locked crate {a[0]}");
                yield break;
            }

            yield return Walk(crate.transform.position);
            var l = crate.GetComponent<Locked>();
            int tries = 0;
            if (WorldActions.Crate(crate) != null && !crate.searched)
            {
                while (!crate.searched && tries < 12)
                {
                    tries++;
                    FinishDisarm(crate);
                    yield return new WaitForSeconds(1.2f);
                }

                P.openedItemInventory = null;
                yield return new WaitForSeconds(1f);
                r.Info =
                    $"{crate.name} {Ref(crate)} picked: "
                        + $"searched={crate.searched} after {tries} tries "
                        + $"(chance {crate.disarmChance:0.00})";
                if (!crate.searched)
                {
                    r.Fail($"{r.Info}: still shut");
                }
            }
            else
            {
                if (CarriedOf("lockpick") == 0)
                {
                    P.Inventory.addItemTypeToPlayer(
                        "lockpick", 1, dropIfNoRoom: true);
                }

                yield return null;
                AsPlayer(() => l.tryToLockpick());
                // a refusal would lock it again
                yield return new WaitForSeconds(1.5f);
                r.Info =
                    $"{crate.name} {Ref(crate)} lockpicked: locked={l.locked}";
                if (l.locked)
                {
                    r.Fail($"{r.Info}: still locked");
                }
            }
        }

        /// <summary>
        /// "pickup TYPE": pick up a dropped item of TYPE with a click
        /// (Item.getDroppedItem).
        /// </summary>
        private static IEnumerator Pickup(string[] a, Result r)
        {
            var it = Find<Item>(
                "DroppedItem",
                i => i.GetComponent<Inventory>() is Inventory inv
                    && inv.slots.Count > 0
                    && !InvItemClass.isNull(inv.slots[0].invItem)
                    && inv.slots[0].invItem.type == a[0]);
            if (it == null)
            {
                r.Fail($"no dropped {a[0]} here");
                yield break;
            }

            yield return Walk(it.transform.position);
            string what = $"{a[0]} {Ref(it)} at {Fmt(it.transform.position)}";
            int before = CarriedOf(a[0]);
            AsPlayer(() => it.getDroppedItem());
            yield return new WaitForSeconds(1.5f);
            int after = CarriedOf(a[0]);
            string state = "still there";
            if (it == null)
            {
                state = "gone";
            }

            r.Info = $"{what}: we carry {before} -> {after}, it's {state}";
            if (it != null || CarriedOf(a[0]) <= before)
            {
                r.Fail($"{r.Info}: not picked up");
            }
        }
    }
}
