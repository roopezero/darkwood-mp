using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioGaps
    {
        // ---- 68 a death bag's map marker ------------------------------------

        private static GameObject _marker;

        /// <summary>
        /// The death bag nearest the transport spot (within 2500 of the
        /// centre).
        /// </summary>
        private static DeathDrop Bag() => NearestAll<DeathDrop>(
            Spot("transport"),
            d => d.GetComponent<Inventory>() != null
                && StateDump.Flat(d.transform.position - Centre) < 2500f);

        /// <summary>"bag-drop": our player leaves a death bag as if they died
        /// in a separate location (Player.dropBody with
        /// OutsideLocations.playerInOutsideLocation and the thing that took
        /// them there): the game puts the bag and a map marker at that
        /// thing.</summary>
        private static IEnumerator BagDrop(string[] a, Result r)
        {
            var p = Player.Instance;
            var bags = p.Inventory.slots.Where(
                s => InvItemClass.isNull(s.invItem)).Take(4).ToList();
            string[] kit = { "bandages", "wire", "rope", "nail" };
            for (int i = 0; i < bags.Count; i++)
            {
                bags[i].inventory = p.Inventory;
                bags[i].createItem(kit[i], 1);
            }

            var o = Singleton<OutsideLocations>.Instance;
            var transport = new GameObject("dwmp_test_transport");
            transport.transform.position = Spot("transport");
            bool wasIn = o.playerInOutsideLocation;
            var wasObj = o.objectThatTransportedMe;
            o.playerInOutsideLocation = true;
            o.objectThatTransportedMe = transport;
            try
            {
                p.dropBody(p.transform.position + new Vector3(80f, 0f, 0f));
            }
            finally
            {
                o.playerInOutsideLocation = wasIn;
                o.objectThatTransportedMe = wasObj;
            }

            // a bag is sent once it has come to rest
            yield return new WaitForSecondsRealtime(3f);
            var bag = NearestAll<DeathDrop>(
                p.transform.position, d => d.GetComponent<Inventory>() != null);
            if (bag != null)
            {
                _marker = bag.additionalMapMarker;
            }
            else
            {
                _marker = null;
            }

            if (bag == null)
            {
                r.Info = "no bag";
            }
            else
            {
                ObjRef id = ObjRef.Of(bag.gameObject);
                string spot = Fmt(bag.transform.position);
                string markerSpot = "none";
                if (_marker != null)
                {
                    markerSpot = Fmt(_marker.transform.position);
                }

                r.Info = $"bag {id} at {spot}, marker {markerSpot}";
            }

            if (bag == null || _marker == null)
            {
                r.Fail(r.Info);
            }

            Object.Destroy(transport);
        }

        /// <summary>"bag-check here|gone": the death bag here, with its marker
        /// at the transport spot (waiting up to 8 s), or the bag and its marker
        /// gone.</summary>
        private static IEnumerator BagCheck(string[] a, Result r)
        {
            bool here = a.Length == 0 || a[0] == "here";
            bool Ok()
            {
                var b = Bag();
                if (!here)
                {
                    return b == null && _marker == null;
                }

                return b != null && b.additionalMapMarker != null
                    && StateDump.Flat(
                        b.additionalMapMarker.transform.position - Spot(
                            "transport")) < 5f;
            }

            float until = Time.realtimeSinceStartup + 8f;
            while (!Ok() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            var bag = Bag();
            if (here && bag != null)
            {
                _marker = bag.additionalMapMarker;
            }

            if (bag == null)
            {
                string text = "gone";
                if (_marker != null)
                {
                    text = "still here";
                }

                r.Info = $"no bag, marker {text}";
            }
            else
            {
                ObjRef id = ObjRef.Of(bag.gameObject);
                string spot = Fmt(bag.transform.position);
                string markerSpot = "none";
                if (bag.additionalMapMarker != null)
                {
                    markerSpot =
                        Fmt(bag.additionalMapMarker.transform.position);
                }

                r.Info = $"bag {id} at {spot}, marker {markerSpot}";
            }

            if (!Ok())
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "bag-take": our player empties the death bag (it goes when emptied,
        /// and its marker with it).
        /// </summary>
        private static IEnumerator BagTake(string[] a, Result r)
        {
            var p = Player.Instance;
            var bag = Bag();
            if (bag == null)
            {
                r.Fail("no bag");
                yield break;
            }

            var inv = bag.GetComponent<Inventory>();
            yield return Walk(bag.transform.position);
            _marker = bag.additionalMapMarker;
            int mark = MapScenarioIssues.LogMark();
            if (inv != null)
            {
                p.openedItemInventory = inv;
                yield return new WaitForSecondsRealtime(0.6f);
                AsPlayer(() =>
                {
                    foreach (var s in inv.slots.ToList())
                    {
                        if (!InvItemClass.isNull(s.invItem))
                        {
                            s.transferItemAllToPlayer();
                        }
                    }
                });

                yield return new WaitForSecondsRealtime(0.6f);
                try
                {
                    if (inv != null)
                    {
                        AsPlayer(() => inv.hide());
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[map] bag-take: hide: {e.Message}");
                }

                p.openedItemInventory = null;
            }

            yield return new WaitForSecondsRealtime(1.5f);
            if (bag != null)
            {
                AsPlayer(() =>
                {
                    Item bagItem = bag.GetComponent<Item>();
                    if (bagItem != null)
                    {
                        bagItem.activate();
                    }
                }); // a bag without items: its experience taken
            }

            yield return new WaitForSecondsRealtime(1.5f);
            var refused = Refused(mark);
            string text = "gone";
            if (bag != null)
            {
                text = "still here";
            }

            string markerState = "gone";
            if (_marker != null)
            {
                markerState = "still here";
            }

            r.Info = $"bag {text}, marker {markerState}";
            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0].Trim()}");
            }
            else if (bag != null || _marker != null)
            {
                r.Fail(r.Info);
            }
        }
    }
}
