using System.Collections;
using System.Globalization;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for one container slot, several players.</summary>
    public static partial class MapScenarioMulti
    {
        // ---- 2. one container slot, several players at the same moment ------

        /// <summary>"mt-grab CONTAINER AT": at epoch second AT, open the
        /// container and take its first thing, as the other players do at the
        /// same moment. Replies "got TYPE" or "rolled back TYPE"; fails if what
        /// we carry and what it holds don't agree with that (the item doubled
        /// or lost here).</summary>
        private static IEnumerator Grab(string[] a, Result r)
        {
            var inv = MapScenarioWorld.Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }

            double at = double.Parse(a[1], CultureInfo.InvariantCulture);
            yield return Walk(inv.transform.position);
            if (Now > at)
            {
                r.Fail($"walked past the moment ({Now - at:0.0} s late)");
                yield break;
            }

            while (Now < at)
            {
                yield return null;
            }

            P.setOpenedItemInventory(inv); // as the game opens it
            // the game's open: the lock is asked for, the slots watched
            yield return null;
            yield return null;
            var slot = inv.slots.FirstOrDefault(
                s => s != null && !InvItemClass.isNull(s.invItem));
            if (slot == null)
            {
                P.openedItemInventory = null;
                r.Fail($"{inv.name} is empty here");
                yield break;
            }

            var item = new InvItemClass(slot.invItem);
            int si = inv.slots.IndexOf(slot);
            int before = MapScenarioWorld.CarriedOf(item.type);
            AsPlayer(() =>
            {
                slot.removeItem();
                P.Inventory.addItemTypeToPlayer(
                    item.type, item.amount, dropIfNoRoom: true);
            });

            yield return new WaitForSeconds(3f);
            if (P.openedItemInventory == inv)
            {
                P.openedItemInventory = null;
            }

            yield return new WaitForSeconds(2f);
            int now = MapScenarioWorld.CarriedOf(item.type);
            bool got = now > before;
            bool back = !InvItemClass.isNull(inv.slots[si].invItem)
                && inv.slots[si].invItem.type == item.type;
            string text = "rolled back";
            if (got)
            {
                text = "got";
            }

            r.Info =
                $"{text} {item.type} x{item.amount} (slot {si} of {inv.name} "
                    + $"{MapScenarioWorld.Ref(inv)}): we carry {before} -> "
                    + $"{now}; it holds [{MapScenarioWorld.Contents(inv)}]";
            if (now != before && now != before + item.amount)
            {
                r.Fail($"{r.Info}: carried amount changed by something else");
            }

            // Rolled back: the slot is the host's again, which the winner
            // emptied (or still holds it, if nobody got it).
            if (got && back)
            {
                r.Fail($"{r.Info}: we got it and it's still in the slot");
            }
        }

        /// <summary>
        /// "mt-locks" (host): how many containers someone has open (each
        /// released once its player closed or left).
        /// </summary>
        private static IEnumerator Locks(string[] a, Result r)
        {
            int n = -1;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                n = coop.Containers.LockCount;
            }

            r.Info = $"{n} container(s) held open";
            if (a.Length > 0 && n != int.Parse(a[0]))
            {
                r.Fail($"{r.Info}, wanted {a[0]}");
            }

            yield break;
        }

        /// <summary>
        /// "mt-leave" (client): leave the session (as quitting to the menu
        /// does).
        /// </summary>
        private static IEnumerator Leave(string[] a, Result r)
        {
            var s = Net.Session.Instance;
            if (s == null || !s.IsClient)
            {
                r.Fail("not a client");
                yield break;
            }

            s.Backend.Leave();
            yield return new WaitForSeconds(3f);
            r.Info = "left";
        }

        /// <summary>
        /// "mt-open-free CONTAINER": open it and keep it open 3 s: nobody else
        /// is said to be using it.
        /// </summary>
        private static IEnumerator OpenFree(string[] a, Result r)
        {
            var inv = MapScenarioWorld.Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }

            yield return Walk(inv.transform.position);
            float t0 = Time.realtimeSinceStartup;
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(3f);
            var msg = MapScenarioWorld.Messages.Where(
                m => m.at >= t0
                    && m.msg.Contains("is using this")).Select(m => m.msg)
                .FirstOrDefault();
            if (P.openedItemInventory == inv)
            {
                P.openedItemInventory = null;
            }

            yield return new WaitForSeconds(0.5f);
            string status = msg;
            if (status == null)
            {
                status = "free";
            }

            r.Info = $"{inv.name} {MapScenarioWorld.Ref(inv)}: {status}";
            if (msg != null)
            {
                r.Fail(r.Info);
            }
        }
    }
}
