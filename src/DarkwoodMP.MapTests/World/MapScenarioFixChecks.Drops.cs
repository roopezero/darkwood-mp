using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Checks of a held throwable dropped.</summary>
    public static partial class MapScenarioFixChecks
    {
        // ---- 40: a held throwable dropped -----------------------------------

        private static readonly
            AccessTools.FieldRef<Player, GameObject> HeldItem =
                AccessTools.FieldRefAccess<Player, GameObject>("heldItem");
        private static readonly System.Reflection.MethodInfo DropItemM =
            AccessTools.Method(
                typeof(Player), "dropItem", new[] { typeof(bool) });

        /// <summary>"drop-held TYPE": take TYPE out, hold it as aiming does
        /// (Player's heldItem), and drop it as the game does when the aim is
        /// let go of (Player.dropItem).</summary>
        private static IEnumerator DropHeld(string[] a, Result r)
        {
            var p = P;
            string type = "flare";
            if (a.Length > 0)
            {
                type = a[0];
            }

            yield return Walk(MapScenarioEnemies.FreeSpot(Spot("drop")), 40f);
            int free =
                p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
            if (free < 0)
            {
                free = p.Hotbar.slots.Count - 1;
            }

            var slot = p.Hotbar.slots[free];
            slot.inventory = p.Hotbar;
            if (!InvItemClass.isNull(slot.invItem))
            {
                slot.removeItem();
            }

            slot.createItem(type, 1);
            foreach (var s in p.Hotbar.slots)
            {
                s.deselect();
            }

            slot.select(noiseless: true);
            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem)
                    || p.currentItem.type != type
                    || p.switchingItem) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (InvItemClass.isNull(p.currentItem)
                || p.currentItem.type != type)
            {
                r.Fail($"couldn't take the {type} out");
                yield break;
            }

            if (!p.currentItem.baseClass.isThrowable)
            {
                r.Fail($"a {type} isn't throwable");
                yield break;
            }

            // As Player's aim does for a throwable.
            var held = Core.AddPrefab(
                p.currentItem.baseClass.item, Vector3.zero,
                Quaternion.Euler(90f, 0f, 0f), p.gameObject);
            if (held == null)
            {
                r.Fail($"couldn't make a held {type}");
                yield break;
            }

            var ren = held.GetComponent<Renderer>();
            if (ren != null)
            {
                ren.enabled = false;
            }

            var hc = held.GetComponent<Collider>();
            var pc = p.GetComponent<Collider>();
            if (hc != null && pc != null)
            {
                Physics.IgnoreCollision(hc, pc);
            }

            HeldItem(p) = held;
            yield return new WaitForSeconds(0.3f);
            AsPlayer(() => DropItemM.Invoke(p, new object[] { true }));
            ThrownItem ti = null;
            if (held != null)
            {
                ti = held.GetComponent<ThrownItem>();
            }

            string name = null;
            if (held != null)
            {
                name = held.name;
            }

            string spot = "-";
            if (held != null)
            {
                spot = Fmt(held.transform.position);
            }

            r.Info =
                $"{type} ({name}) at {spot}: held {HeldItem(p) != null}, on "
                    + $"the ground {(ti != null && ti.onGround)}";
            if (HeldItem(p) != null)
            {
                r.Fail($"still held: {r.Info}");
            }

            yield return new WaitForSeconds(1.5f);
            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "expect-dropped TYPE": the other player's dropped TYPE lies here by
        /// them, dropped (ThrownItem.dropMe).
        /// </summary>
        private static IEnumerator ExpectDropped(string[] a, Result r)
        {
            string type = "flare";
            if (a.Length > 0)
            {
                type = a[0];
            }

            var it = Singleton<ItemsDatabase>.Instance.getItem(
                type, instantiate: false);
            if (it == null || it.item == null)
            {
                r.Fail($"no {type} in the database");
                yield break;
            }

            string prefab = it.item.name;
            GameObject found = null;
            float until = Time.realtimeSinceStartup + 4f;
            while (found == null && Time.realtimeSinceStartup < until)
            {
                found = ThrownHits.Copies.Where(
                    g => g != null
                        && g.name.StartsWith(prefab, StringComparison.Ordinal)
                        && StateDump.Flat(g.transform.position - Spot("drop"))
                            < 500f).FirstOrDefault();
                if (found == null)
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }

            if (found == null)
            {
                r.Fail(
                    $"no copy of a dropped {type} ({prefab}) by the drop spot "
                        + "here");
                yield break;
            }

            var ti = found.GetComponent<ThrownItem>();
            var rb = found.GetComponent<Rigidbody>();
            string name = found.name;
            string spot = Fmt(found.transform.position);
            bool grounded = ti != null && ti.onGround;
            float speed = 0f;
            if (rb != null)
            {
                speed = rb.velocity.magnitude;
            }

            r.Info =
                $"{name} at {spot}: on the ground {grounded}, speed {speed:0}";
            if (ti == null || !ti.onGround)
            {
                r.Fail($"it wasn't dropped (ThrownItem.dropMe) here: {r.Info}");
            }
        }
    }
}
