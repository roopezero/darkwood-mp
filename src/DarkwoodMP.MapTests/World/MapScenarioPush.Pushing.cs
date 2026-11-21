using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Sync.Players;
using DarkwoodMP.World.Doors;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Steps that walk into loose things and push them.</summary>
    public static partial class MapScenarioPush
    {
        /// This session's DragSync; null without a session.
        private static DragSync Drags()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return null;
            }

            return coop.Drags;
        }

        /// <summary>
        /// Our player's body moved along dir at PushSpeed by the physics (its
        /// rigidbody's velocity, set every physics step, as the game's own
        /// walking force does), so whatever is in the way is pushed, for secs
        /// or until stop().
        /// </summary>
        private static IEnumerator BodyWalk(
            Vector3 dir, float secs, Func<bool> stop)
        {
            var rb = P.GetComponent<Rigidbody>();
            if (rb == null)
            {
                yield break;
            }

            dir.y = 0f;
            dir = dir.normalized;
            float until = Time.time + secs;
            while (Time.time < until && (stop == null || !stop()))
            {
                yield return new WaitForFixedUpdate();
                if (rb == null)
                {
                    yield break;
                }

                rb.WakeUp();
                rb.velocity = new Vector3(
                    dir.x * PushSpeed, rb.velocity.y, dir.z * PushSpeed);
            }

            if (rb != null)
            {
                rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            }
        }

        /// <summary>
        /// The way to push it: DEG if a player can stand back from it on that
        /// side and it has room to go (no wall, shut door or fixed furniture
        /// between), else the nearest of every 45 degrees from there that has.
        /// The rooms are the game's: a wall right behind a thing (a lamp in a
        /// corner) would make a push from there test nothing.
        /// </summary>
        private static Vector3 ClearWay(
            Item it, float deg, float back, float ahead)
        {
            var at = it.transform.position;
            foreach (int k in new[] { 0, 45, -45, 90, -90, 135, -135, 180 })
            {
                var d = Dir(deg + k);
                if (Clear(it, at - d * back, at)
                    && (ahead <= 0f || Clear(
                        it, at, at + d * (Size(it) + ahead))))
                {
                    return d;
                }
            }

            return Dir(deg);
        }

        /// <summary>
        /// Nothing solid between a and b at the player's height but the thing,
        /// players and loose things.
        /// </summary>
        private static bool Clear(Item it, Vector3 a, Vector3 b)
        {
            float y = P.transform.position.y;
            a.y = y;
            b.y = y;
            var d = b - a;
            foreach (var h in Physics.SphereCastAll(
                a, 15f, d.normalized, d.magnitude, ~0,
                QueryTriggerInteraction.Ignore))
            {
                var c = h.collider;
                if (c == null || c.transform.IsChildOf(it.transform)
                    || c.GetComponentInParent<Player>() != null)
                {
                    continue;
                }

                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic
                    && rb.constraints != RigidbodyConstraints.FreezeAll
                    && rb.GetComponentInParent<Door>() == null)
                {
                    continue;
                }

                if (c.GetComponentInParent<CharBase>() != null)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static IEnumerator Bump(string[] a, Result r, bool lean)
        {
            var it = ByIndex(a, r);
            if (it == null)
            {
                yield break;
            }

            float dist = 60f;
            if (!lean && a.Length > 2)
            {
                dist = F(a[2]);
            }

            float hold = 5f;
            if (lean && a.Length > 2)
            {
                hold = F(a[2]);
            }

            var start = it.transform.position;
            float back = Size(it) + 60f;
            float angle = 0f;
            if (a.Length > 1)
            {
                angle = F(a[1]);
            }

            float ahead;
            if (lean)
            {
                ahead = 0f;
            }
            else
            {
                ahead = dist + 20f;
            }

            var dir = ClearWay(it, angle, back, ahead);
            yield return Walk(start - dir * back, 10f, detours: 0);
            // at rest where the walk left us
            yield return new WaitForSeconds(0.5f);
            start = it.transform.position;
            float moved() => StateDump.Flat(it.transform.position - start);
            yield return BodyWalk(dir, 6f, () => it == null || moved() >= dist);
            if (it == null)
            {
                r.Fail("it's gone");
                yield break;
            }

            if (lean)
            {
                // Pressed against it while it's still: a body held there. A
                // thing in the way stops it, else it goes on.
                yield return BodyWalk(dir, hold, null);
            }

            var rb = it.GetComponent<Rigidbody>();
            yield return BodyWalk(-dir, 0.4f, null); // back off it
            float rest = Time.time + 6f;
            while (Time.time < rest && rb != null
                && !(rb.IsSleeping() || rb.velocity.sqrMagnitude < 1f))
            {
                yield return null;
            }

            // seen at rest (four checks a second) and sent
            yield return new WaitForSeconds(1.5f);
            float heading = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
            r.Info =
                FormattableString.Invariant(
                    $"{it.name} {Ref(it)} pushed towards {heading:0}: ")
                + FormattableString.Invariant(
                    $"from {Fmt1(start)} to {Fmt1(it.transform.position)} ")
                + FormattableString.Invariant($"({moved():0.0} units), ")
                + FormattableString.Invariant(
                    $"we are at {Fmt1(P.transform.position)}");
            if (moved() < 3f)
            {
                r.Fail($"{r.Info}: it didn't move");
            }
        }

        private static IEnumerator Hit(string[] a, Result r)
        {
            var it = ByIndex(a, r);
            if (it == null)
            {
                yield break;
            }

            var rb = it.GetComponent<Rigidbody>();
            var start = it.transform.position;
            var from = start - Dir(0f) * (Size(it) + 150f);
            yield return Walk(from, 10f, detours: 0);
            yield return new WaitForSeconds(0.5f);
            start = it.transform.position;
            var point = it.GetComponentsInChildren<Collider>()
                .Where(c => !c.isTrigger)
                .Select(c => c.ClosestPoint(P.transform.position))
                .DefaultIfEmpty(start).First();
            AsPlayer(() =>
            {
                it.getHit(1, P.transform);
                if (rb != null)
                {
                    rb.AddForceAtPosition(
                        (P.transform.position - point).normalized * -20000f,
                        point);
                }
            });

            float rest = Time.time + 6f;
            yield return new WaitForSeconds(0.3f);
            while (Time.time < rest && rb != null
                && !(rb.IsSleeping() || rb.velocity.sqrMagnitude < 1f))
            {
                yield return null;
            }

            yield return new WaitForSeconds(1.5f);
            float moved = StateDump.Flat(it.transform.position - start);
            r.Info =
                $"{it.name} {Ref(it)} hit from "
                    + $"{StateDump.Flat(P.transform.position - start):0}: from "
                    + $"{Fmt1(start)} to {Fmt1(it.transform.position)} "
                    + $"({moved:0.0} units)";
            if (moved < 3f)
            {
                r.Fail($"{r.Info}: it didn't move");
            }
        }

        private static IEnumerator DoorWalk(string[] a, Result r)
        {
            float deg = 0f;
            if (a.Length > 0)
            {
                deg = F(a[0]);
            }

            var dir = Dir(deg);
            var d = Nearest(
                Object.FindObjectsOfType<Door>().Where(
                    x => x.opened && !x.destroyed && !x.barricaded
                        && x.body != null && ObjRef.Of(x.gameObject).Valid),
                Centre);
            if (d == null)
            {
                r.Fail("no open door");
                yield break;
            }

            float before = DoorLeaf.Signed(d);
            // From DEG's side, and if the leaf doesn't turn (it's against the
            // wall that way), from the other.
            for (int side = 0; side < 2
                && Mathf.Abs(Mathf.DeltaAngle(before, DoorLeaf.Signed(d)))
                    < 6f; side++, dir = -dir)
            {
                var leaf = d.body.position;
                yield return Walk(leaf - dir * 120f, 10f, detours: 0);
                yield return new WaitForSeconds(0.3f);
                // into the leaf and through
                yield return BodyWalk(dir, 1.2f, null);
                // the leaf at rest (0.4 s still) and sent
                yield return new WaitForSeconds(2.5f);
            }

            r.Info =
                $"{d.name} "
                    + $"{ObjRef.Of(d.gameObject)}: "
                    + $"leaf {before:0} -> {DoorLeaf.Signed(d):0}, "
                    + $"we are at {Fmt1(P.transform.position)}";
            if (Mathf.Abs(Mathf.DeltaAngle(before, DoorLeaf.Signed(d))) < 6f)
            {
                r.Fail($"{r.Info}: the leaf didn't turn");
            }
        }

        private static IEnumerator Overlap(string[] a, Result r)
        {
            var sync = Plugin.Instance.Host.Players;
            RemotePlayer near = null;
            float best = float.MaxValue;
            if (sync != null)
            {
                foreach (var rp in sync.Remotes.Values)
                {
                    if (rp.Root == null || !rp.Root.activeSelf || !rp.Alive)
                    {
                        continue;
                    }

                    float d = StateDump.Flat(
                        rp.Root.transform.position - P.transform.position);
                    if (d < best)
                    {
                        best = d;
                        near = rp;
                    }
                }
            }

            if (near == null)
            {
                r.Fail("no other player shown here");
                yield break;
            }

            yield return Walk(near.Root.transform.position, 60f);
            var on = near.Root.transform.position + new Vector3(2f, 0f, 1f);
            Place(on);
            PlayerSeparation apart =
                Plugin.Instance.Host.Players.Separation;
            float eased = apart.Eased, want = apart.Distance * 0.75f;
            float dist() => StateDump.Flat(
                near.Root.transform.position - P.transform.position);
            float until = Time.unscaledTime + 4f;
            while (Time.unscaledTime < until && dist() < want)
            {
                yield return null;
            }

            float gap = StateDump.Flat(near.Root.transform.position - on);
            r.Info =
                $"put on {near.Name} "
                    + $"({gap:0.0} apart): {dist():0.0} apart after "
                    + $"{4f - (until - Time.unscaledTime):0.0} s, "
                    + $"eased {apart.Eased - eased:0.0} here "
                    + $"(apart at {apart.Distance:0})";
            if (dist() < want)
            {
                r.Fail($"{r.Info}: not eased apart");
            }

            yield return new WaitForSeconds(1f);
            if (dist() > apart.Distance * 3f)
            {
                r.Fail($"{r.Info}, then {dist():0.0}: pushed too far");
            }
        }

        private static IEnumerator At(string[] a, Result r)
        {
            var it = ByIndex(a, r);
            if (it == null)
            {
                yield break;
            }

            var rb = it.GetComponent<Rigidbody>();
            string name = it.name;
            string id = Ref(it);
            string pos = Fmt1(it.transform.position);
            float yaw = it.transform.rotation.eulerAngles.y;
            string watch = null;
            DragSync drag = Drags();
            if (drag != null)
            {
                watch = drag.WatchInfo(rb);
            }

            r.Info = FormattableString.Invariant(
                $"{name} {id} at {pos} yaw {yaw:0}, {watch}");
            yield break;
        }
    }
}
