using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for things players bump into and shove (tools/tests/map4_push.py): furniture, crates, placed
    /// objects and open doors' leaves, moved by the player's body through the physics, as walking into them does
    /// (the player's rigidbody pushed along, not put in place frame by frame). Run in map4, so a push by one client
    /// is checked in the host's game and in every other client's, and a second player then bumps the same thing
    /// where their game has it.
    ///
    ///   ps-list [R] [new]        the loose things within R (400) of the centre that a player's body can move: each
    ///                            one's ObjRef, where it is, its mass, and whether this game watches it for pushes
    ///                            (DragSync) or as a door's leaf (DoorPush). The first call notes their order
    ///                            (nearest the centre first), which INDEX counts in, so every game picks the same one.
    ///                            Fails if one isn't watched. "new" notes the order again (at another centre).
    ///   ps-bump INDEX DEG [DIST] our player walks into thing INDEX from the side away from DEG (degrees about up,
    ///                            0 = +x, 90 = -z) and goes on along DEG, its body pushing it, until it moved DIST (60)
    ///                            or 6 s went by; then backs off and waits for it to come to rest. If a wall is in
    ///                            the way on that side, the nearest way round (in 45 degree steps) that's clear is
    ///                            taken. Fails if it didn't move.
    ///   ps-lean INDEX DEG [SECS] the same, but keeps leaning on it for SECS (5) after it has moved, the player
    ///                            pressed against it while it's still (pushed into a wall or a corner, or just held),
    ///                            and backs off only then.
    ///   ps-hit INDEX             our player hits it (Item.getHit and the shot's push, Player's AddForceAtPosition)
    ///                            from 150 away.
    ///   ps-door DEG              our player walks into the open leaf of the door nearest the centre, through it along
    ///                            DEG, its body turning the leaf (from the other side if it doesn't turn that way).
    ///   ps-at INDEX              where thing INDEX is here (for the log: the dumps compare it).
    ///   ps-overlap               soft collision between players (Sync/PlayerSeparation): our player is put right
    ///                            on the nearest other player's puppet and stands still; within 4 s the two must be
    ///                            eased at least most of the way apart (both games move their own player).
    /// </summary>
    public static class MapScenarioPush
    {
        private static Player P => Player.Instance;
        private static readonly List<string> _order = new List<string>();
        private const float PushSpeed = 250f; // a walk (MoveCheck lets much more through)

        public static void Register()
        {
            Add("ps-list", List, timeout: 10f);
            Add("ps-bump", (a, r) => Bump(a, r, lean: false), timeout: 40f);
            Add("ps-lean", (a, r) => Bump(a, r, lean: true), timeout: 50f);
            Add("ps-hit", Hit, timeout: 30f);
            Add("ps-door", DoorWalk, timeout: 40f);
            Add("ps-at", At, timeout: 5f);
            Add("ps-overlap", Overlap, timeout: 30f);
            StateDump.AddSection("loose", ctx =>
            {
                // Every thing a player can drag or push, with its turn (the items section has where it is). Not by
                // its body's state: a game's culling switches far things off and may leave their bodies kinematic.
                foreach (var it in StateDump.All<Item>())
                {
                    var b = it.GetComponent<Rigidbody>();
                    if (b == null || !it.draggable || it.isDroppedItem || !ctx.Near(it.transform.position) || it.GetComponentInParent<Player>() != null) continue;
                    var e = ctx.Add(it.gameObject);
                    e["yaw"] = StateDump.R(b.transform.rotation.eulerAngles.y, 1f);
                }
            });
            StateDump.AddSection("leaves", ctx =>
            {
                // Every open door's leaf, at its angle from shut (the doors section has only whether it's drawn open).
                foreach (var d in StateDump.All<Door>())
                {
                    if (!ctx.Near(d.transform.position) || !d.opened || d.destroyed || d.body == null) continue;
                    ctx.Add(d.gameObject)["leaf"] = StateDump.R(DoorLeaf.Signed(d), 1f);
                }
            });
        }

        private static string Fmt1(Vector3 p) => FormattableString.Invariant($"{p.x:0.0} {p.z:0.0}");
        private static string Ref(Component c) => ObjRef.Of(c.gameObject).ToString();
        private static Vector3 Dir(float deg) => Quaternion.Euler(0f, deg, 0f) * Vector3.right;

        /// <summary>An Item with a rigidbody that the physics can move, not a character, door, window or dropped item.</summary>
        private static bool Movable(Rigidbody b, out Item it)
        {
            it = null;
            if (b == null || b.isKinematic || b.constraints == RigidbodyConstraints.FreezeAll) return false;
            if (b.GetComponent<CharBase>() != null || b.GetComponentInParent<Door>() != null || b.GetComponentInParent<Window>() != null) return false;
            it = b.GetComponent<Item>();
            if (it == null || it.GetComponentInParent<Player>() != null || it.isDroppedItem) return false;
            return Solid(b);
        }

        /// <summary>The body has a collider the player's body bumps into (not a trigger, on a layer that collides with the player's), switched on or off.</summary>
        private static bool Solid(Rigidbody b)
        {
            int pl = P != null ? P.gameObject.layer : 0;
            foreach (var c in b.GetComponentsInChildren<Collider>(true))
                if (c.GetComponentInParent<Rigidbody>(true) == b && !c.isTrigger && !Physics.GetIgnoreLayerCollision(pl, c.gameObject.layer)) return true;
            return false;
        }

        /// <summary>Every body near the centre that a player's body can move, whatever it is.</summary>
        private static IEnumerable<Rigidbody> Bodies(float radius) =>
            Object.FindObjectsOfType<Rigidbody>().Where(b => b != null && !b.isKinematic && b.constraints != RigidbodyConstraints.FreezeAll
                && StateDump.Flat(b.position - Centre) < radius && b.GetComponentInParent<Player>() == null && Solid(b));

        private static IEnumerator List(string[] a, Result r)
        {
            float radius = a.Length > 0 && a[0] != "new" ? F(a[0]) : 400f;
            var things = new List<Item>();
            var unwatched = new List<string>();
            foreach (var b in Bodies(radius).OrderBy(b => Math.Round(StateDump.Flat(b.position - Centre))).ThenBy(b => ObjRef.Of(b.gameObject).ToString(), StringComparer.Ordinal))
            {
                string what;
                if (b.GetComponent<CharBase>() != null) what = "character";
                else if (b.GetComponentInParent<Door>() is Door d) what = $"door {d.name} leaf, {(DoorPush.Watches(d) ? "watched" : d.opened ? "NOT WATCHED" : "shut")}";
                else if (b.GetComponentInParent<Window>() != null) what = "window";
                else if (Movable(b, out var it))
                {
                    string w = DragSync.Instance != null ? DragSync.Instance.WatchInfo(b) : "no DragSync";
                    what = $"{(it.draggable ? "draggable" : "loose")}, {w}";
                    things.Add(it);
                    if (w == "not watched" || w == "no body") unwatched.Add($"{it.name} {Ref(it)}");
                }
                else
                {
                    what = $"NOT SHARED (no Item{(b.GetComponentInParent<Item>() != null ? " on the body, one above it" : "")})";
                    unwatched.Add($"{b.name} {Ref(b)}");
                }
                Plugin.Log.LogInfo(FormattableString.Invariant($"[push] {b.name} {Ref(b)} at {Fmt1(b.position)} mass {b.mass:0.##} drag {b.drag:0.##} layer {b.gameObject.layer}: {what}"));
            }
            if (_order.Count == 0 || a.Contains("new")) { _order.Clear(); _order.AddRange(things.Select(Ref)); }
            r.Info = string.Join("; ", things.Select(i => $"{_order.IndexOf(Ref(i))}={i.name} {Ref(i)} {Fmt1(i.transform.position)}"));
            if (things.Count == 0) r.Fail("nothing loose by the centre");
            else if (unwatched.Count > 0) r.Fail($"not watched for pushes here: {string.Join(", ", unwatched)}");
            yield break;
        }

        private static Item ByIndex(string[] a, Result r)
        {
            int k = a.Length > 0 ? int.Parse(a[0], CultureInfo.InvariantCulture) : 0;
            if (k < 0 || k >= _order.Count) { r.Fail($"no thing {k} (ps-list found {_order.Count})"); return null; }
            var it = StateDump.All<Item>().FirstOrDefault(i => i != null && Ref(i) == _order[k]);
            if (it == null) r.Fail($"thing {k} ({_order[k]}) not here");
            return it;
        }

        /// <summary>How far the thing reaches out from its centre along the ground (its colliders' bounds).</summary>
        private static float Size(Item it)
        {
            float s = 20f;
            foreach (var c in it.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) s = Mathf.Max(s, Mathf.Max(c.bounds.extents.x, c.bounds.extents.z));
            return s;
        }

        /// <summary>
        /// Our player's body moved along dir at PushSpeed by the physics (its rigidbody's velocity, set every physics
        /// step, as the game's own walking force does), so whatever is in the way is pushed, for secs or until stop().
        /// </summary>
        private static IEnumerator BodyWalk(Vector3 dir, float secs, Func<bool> stop)
        {
            var rb = P.GetComponent<Rigidbody>();
            if (rb == null) yield break;
            dir.y = 0f; dir = dir.normalized;
            float until = Time.time + secs;
            while (Time.time < until && (stop == null || !stop()))
            {
                yield return new WaitForFixedUpdate();
                if (rb == null) yield break;
                rb.WakeUp();
                rb.velocity = new Vector3(dir.x * PushSpeed, rb.velocity.y, dir.z * PushSpeed);
            }
            if (rb != null) rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        }

        /// <summary>
        /// The way to push it: DEG if a player can stand back from it on that side and it has room to go (no wall,
        /// shut door or fixed furniture between), else the nearest of every 45 degrees from there that has. The rooms
        /// are the game's: a wall right behind a thing (a lamp in a corner) would make a push from there test nothing.
        /// </summary>
        private static Vector3 ClearWay(Item it, float deg, float back, float ahead)
        {
            var at = it.transform.position;
            foreach (int k in new[] { 0, 45, -45, 90, -90, 135, -135, 180 })
            {
                var d = Dir(deg + k);
                if (Clear(it, at - d * back, at) && (ahead <= 0f || Clear(it, at, at + d * (Size(it) + ahead)))) return d;
            }
            return Dir(deg);
        }

        /// <summary>Nothing solid between a and b at the player's height but the thing, players and loose things.</summary>
        private static bool Clear(Item it, Vector3 a, Vector3 b)
        {
            float y = P.transform.position.y;
            a.y = y; b.y = y;
            var d = b - a;
            foreach (var h in Physics.SphereCastAll(a, 15f, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                var c = h.collider;
                if (c == null || c.transform.IsChildOf(it.transform) || c.GetComponentInParent<Player>() != null) continue;
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic && rb.constraints != RigidbodyConstraints.FreezeAll && rb.GetComponentInParent<Door>() == null) continue;
                if (c.GetComponentInParent<CharBase>() != null) continue;
                return false;
            }
            return true;
        }

        private static IEnumerator Bump(string[] a, Result r, bool lean)
        {
            var it = ByIndex(a, r);
            if (it == null) yield break;
            float dist = !lean && a.Length > 2 ? F(a[2]) : 60f;
            float hold = lean && a.Length > 2 ? F(a[2]) : 5f;
            var start = it.transform.position;
            float back = Size(it) + 60f;
            var dir = ClearWay(it, a.Length > 1 ? F(a[1]) : 0f, back, lean ? 0f : dist + 20f);
            yield return Walk(start - dir * back, 10f, detours: 0);
            yield return new WaitForSeconds(0.5f); // at rest where the walk left us
            start = it.transform.position;
            float moved() => StateDump.Flat(it.transform.position - start);
            yield return BodyWalk(dir, 6f, () => it == null || moved() >= dist);
            if (it == null) { r.Fail("it's gone"); yield break; }
            if (lean)
            {
                // Pressed against it while it's still: a body held there. A thing in the way stops it, else it goes on.
                yield return BodyWalk(dir, hold, null);
            }
            var rb = it.GetComponent<Rigidbody>();
            yield return BodyWalk(-dir, 0.4f, null); // back off it
            float rest = Time.time + 6f;
            while (Time.time < rest && rb != null && !(rb.IsSleeping() || rb.velocity.sqrMagnitude < 1f)) yield return null;
            yield return new WaitForSeconds(1.5f); // seen at rest (four checks a second) and sent
            r.Info = FormattableString.Invariant($"{it.name} {Ref(it)} pushed towards {Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg:0}: from {Fmt1(start)} to {Fmt1(it.transform.position)} ({moved():0.0} units), we are at {Fmt1(P.transform.position)}");
            if (moved() < 3f) r.Fail($"{r.Info}: it didn't move");
        }

        private static IEnumerator Hit(string[] a, Result r)
        {
            var it = ByIndex(a, r);
            if (it == null) yield break;
            var rb = it.GetComponent<Rigidbody>();
            var start = it.transform.position;
            var from = start - Dir(0f) * (Size(it) + 150f);
            yield return Walk(from, 10f, detours: 0);
            yield return new WaitForSeconds(0.5f);
            start = it.transform.position;
            var point = it.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).Select(c => c.ClosestPoint(P.transform.position)).DefaultIfEmpty(start).First();
            AsPlayer(() =>
            {
                it.getHit(1, P.transform);
                if (rb != null) rb.AddForceAtPosition((P.transform.position - point).normalized * -20000f, point);
            });
            float rest = Time.time + 6f;
            yield return new WaitForSeconds(0.3f);
            while (Time.time < rest && rb != null && !(rb.IsSleeping() || rb.velocity.sqrMagnitude < 1f)) yield return null;
            yield return new WaitForSeconds(1.5f);
            float moved = StateDump.Flat(it.transform.position - start);
            r.Info = $"{it.name} {Ref(it)} hit from {StateDump.Flat(P.transform.position - start):0}: from {Fmt1(start)} to {Fmt1(it.transform.position)} ({moved:0.0} units)";
            if (moved < 3f) r.Fail($"{r.Info}: it didn't move");
        }

        private static IEnumerator DoorWalk(string[] a, Result r)
        {
            var dir = Dir(a.Length > 0 ? F(a[0]) : 0f);
            var d = Nearest(Object.FindObjectsOfType<Door>().Where(x => x.opened && !x.destroyed && !x.barricaded && x.body != null && ObjRef.Of(x.gameObject).Valid), Centre);
            if (d == null) { r.Fail("no open door"); yield break; }
            float before = DoorLeaf.Signed(d);
            // From DEG's side, and if the leaf doesn't turn (it's against the wall that way), from the other.
            for (int side = 0; side < 2 && Mathf.Abs(Mathf.DeltaAngle(before, DoorLeaf.Signed(d))) < 6f; side++, dir = -dir)
            {
                var leaf = d.body.position;
                yield return Walk(leaf - dir * 120f, 10f, detours: 0);
                yield return new WaitForSeconds(0.3f);
                yield return BodyWalk(dir, 1.2f, null); // into the leaf and through
                yield return new WaitForSeconds(2.5f); // the leaf at rest (0.4 s still) and sent
            }
            r.Info = $"{d.name} {ObjRef.Of(d.gameObject)}: leaf {before:0} -> {DoorLeaf.Signed(d):0}, we are at {Fmt1(P.transform.position)}";
            if (Mathf.Abs(Mathf.DeltaAngle(before, DoorLeaf.Signed(d))) < 6f) r.Fail($"{r.Info}: the leaf didn't turn");
        }

        private static IEnumerator Overlap(string[] a, Result r)
        {
            var sync = Sync.PlayerSync.Instance;
            Sync.RemotePlayer near = null; float best = float.MaxValue;
            if (sync != null)
                foreach (var rp in sync.Remotes.Values)
                {
                    if (rp.Root == null || !rp.Root.activeSelf || !rp.Alive) continue;
                    float d = StateDump.Flat(rp.Root.transform.position - P.transform.position);
                    if (d < best) { best = d; near = rp; }
                }
            if (near == null) { r.Fail("no other player shown here"); yield break; }
            yield return Walk(near.Root.transform.position, 60f);
            var on = near.Root.transform.position + new Vector3(2f, 0f, 1f);
            Place(on);
            float eased = Sync.PlayerSeparation.Eased, want = Sync.PlayerSeparation.Distance * 0.75f;
            float dist() => StateDump.Flat(near.Root.transform.position - P.transform.position);
            float until = Time.unscaledTime + 4f;
            while (Time.unscaledTime < until && dist() < want) yield return null;
            r.Info = $"put on {near.Name} ({StateDump.Flat(near.Root.transform.position - on):0.0} apart): {dist():0.0} apart after {4f - (until - Time.unscaledTime):0.0} s, eased {Sync.PlayerSeparation.Eased - eased:0.0} here (apart at {Sync.PlayerSeparation.Distance:0})";
            if (dist() < want) r.Fail($"{r.Info}: not eased apart");
            yield return new WaitForSeconds(1f);
            if (dist() > Sync.PlayerSeparation.Distance * 3f) r.Fail($"{r.Info}, then {dist():0.0}: pushed too far");
        }

        private static IEnumerator At(string[] a, Result r)
        {
            var it = ByIndex(a, r);
            if (it == null) yield break;
            var rb = it.GetComponent<Rigidbody>();
            r.Info = FormattableString.Invariant($"{it.name} {Ref(it)} at {Fmt1(it.transform.position)} yaw {it.transform.rotation.eulerAngles.y:0}, {DragSync.Instance?.WatchInfo(rb)}");
            yield break;
        }
    }
}
