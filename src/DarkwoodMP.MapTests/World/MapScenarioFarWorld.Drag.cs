using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Steps for an object dragged where it is switched off.</summary>
    public static partial class MapScenarioFarWorld
    {
        /// <summary>
        /// "far-drag FROM TO": the draggable object nearest FROM dragged to TO
        /// (both spots) by our player, the way MapScenarioWorld's drag does it,
        /// over a few seconds.
        /// </summary>
        private static IEnumerator FarDrag(string[] a, Result r)
        {
            var from = Spot(a[0]);
            var to = Spot(a[1]);
            // One on the culling grid (a node of its own), not one switched on
            // and off with its location's objects.
            var it = Nearest(Object.FindObjectsOfType<Item>(true).Where(i =>
            {
                if (i.draggable && i.GetComponent<Rigidbody>() != null)
                {
                    CullableObject cull = i.GetComponent<CullableObject>();
                    if (cull != null && cull.node != null)
                    {
                        return ObjRef.Of(i.gameObject).Valid
                            && StateDump.Flat(i.transform.position - from)
                                < 2500f;
                    }
                }

                return false;
            }), from);

            if (it == null)
            {
                r.Fail($"nothing draggable within 2500 of {Fmt(from)}");
                yield break;
            }

            yield return Walk(it.transform.position, 80f);
            var start = it.transform.position;
            var by = new Vector3(to.x - start.x, 0f, to.z - start.z);
            var rb = it.GetComponent<Rigidbody>();
            P.itemBeingDragged = it;
            const float secs = 4f;
            for (float t = 0f; t < secs; t += Time.deltaTime)
            {
                if (P.itemBeingDragged != it)
                {
                    r.Fail(
                        $"{it.name}: our drag was let go of (another player "
                            + "has it)");
                    yield break;
                }

                var at = start + by * (t / secs);
                it.transform.position = at;
                if (rb != null)
                {
                    rb.position = at;
                }

                P.transform.position = new Vector3(
                    at.x, P.transform.position.y, at.z) - by.normalized * 70f;
                yield return null;
            }

            it.transform.position = start + by;
            if (rb != null)
            {
                rb.position = start + by;
                rb.velocity = Vector3.zero;
            }

            yield return new WaitForSeconds(0.3f);
            P.itemBeingDragged = null;
            // where it came to rest goes out once settled
            yield return new WaitForSeconds(2.5f);
            r.Info =
                $"{it.name} "
                    + $"{ObjRef.Of(it.gameObject)} from {Fmt(start)} "
                    + $"to {Fmt(it.transform.position)}";
            if (StateDump.Flat(it.transform.position - to) > 300f)
            {
                r.Fail($"{r.Info}: it didn't get to {Fmt(to)}");
            }
        }

        /// <summary>
        /// "far-obj-shown SPOT": walk to the spot; the draggable object nearest
        /// it must be there and switched on.
        /// </summary>
        private static IEnumerator FarObjShown(string[] a, Result r)
        {
            var at = Spot(a[0]);
            yield return Walk(at, 150f);
            // the culling enters a node a frame, every half second
            yield return new WaitForSeconds(3f);
            var it = Nearest(
                Object.FindObjectsOfType<Item>(true).Where(
                    i => i.draggable && i.GetComponent<Rigidbody>() != null),
                at);
            if (it == null)
            {
                r.Fail("nothing draggable");
                yield break;
            }

            var co = it.GetComponent<CullableObject>();
            WorldGrid.Node node = null;
            if (co != null)
            {
                node = co.node;
            }

            string state =
                $"{it.name} "
                    + $"{ObjRef.Of(it.gameObject)} "
                    + $"at {Fmt(it.transform.position)} "
                    + $"on={it.gameObject.activeInHierarchy}";
            string culling = " no node";
            if (node != null)
            {
                culling =
                    $" node {node.position.x:0},{node.position.y:0} "
                        + $"entered={node.entered}";
            }

            r.Info = state + culling;
            if (StateDump.Flat(it.transform.position - at) > 300f)
            {
                r.Fail($"{r.Info}: not at {Fmt(at)}");
            }
            else if (!it.gameObject.activeInHierarchy)
            {
                r.Fail($"{r.Info}: switched off where we stand");
            }
        }
    }
}
