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
    /// Map test steps for things players bump into and shove
    /// (tools/tests/map4_push.py): furniture, crates, placed objects and open
    /// doors' leaves, moved by the player's body through the physics, as
    /// walking into them does (the player's rigidbody pushed along, not put in
    /// place frame by frame). Run in map4, so a push by one client is checked
    /// in the host's game and in every other client's, and a second player then
    /// bumps the same thing where their game has it.
    ///
    ///   ps-list [R] [new]        the loose things within R (400) of the centre
    ///                            that a player's body can move: each one's
    ///                            ObjRef, where it is, its mass, and whether
    ///                            this game watches it for pushes (DragSync) or
    ///                            as a door's leaf (DoorPush). The first call
    ///                            notes their order (nearest the centre first),
    ///                            which INDEX counts in, so every game picks
    ///                            the same one. Fails if one isn't watched.
    ///                            "new" notes the order again (at another
    ///                            centre).
    ///   ps-bump INDEX DEG [DIST] our player walks into thing INDEX from the
    ///                            side away from DEG (degrees about up, 0 = +x,
    ///                            90 = -z) and goes on along DEG, its body
    ///                            pushing it, until it moved DIST (60) or 6 s
    ///                            went by; then backs off and waits for it to
    ///                            come to rest. If a wall is in the way on that
    ///                            side, the nearest way round (in 45 degree
    ///                            steps) that's clear is taken. Fails if it
    ///                            didn't move.
    ///   ps-lean INDEX DEG [SECS] the same, but keeps leaning on it for SECS
    ///                            (5) after it has moved, the player pressed
    ///                            against it while it's still (pushed into a
    ///                            wall or a corner, or just held), and backs
    ///                            off only then.
    ///   ps-hit INDEX             our player hits it (Item.getHit and the
    ///                            shot's push, Player's AddForceAtPosition)
    ///                            from 150 away.
    ///   ps-door DEG              our player walks into the open leaf of the
    ///                            door nearest the centre, through it along
    ///                            DEG, its body turning the leaf (from the
    ///                            other side if it doesn't turn that way).
    ///   ps-at INDEX              where thing INDEX is here (for the log: the
    ///                            dumps compare it).
    ///   ps-overlap               soft collision between players
    ///                            (Sync/PlayerSeparation): our player is put
    ///                            right on the nearest other player's puppet
    ///                            and stands still; within 4 s the two must be
    ///                            eased at least most of the way apart (both
    ///                            games move their own player).
    /// </summary>
    public static partial class MapScenarioPush
    {
        private static Player P => Player.Instance;
        private static readonly List<string> _order = new List<string>();
        // a walk (MoveCheck lets much more through)
        private const float PushSpeed = 250f;

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
                // Every thing a player can drag or push, with its turn (the
                // items section has where it is). Not by its body's state: a
                // game's culling switches far things off and may leave their
                // bodies kinematic.
                foreach (var it in StateDump.All<Item>())
                {
                    var b = it.GetComponent<Rigidbody>();
                    if (b == null || !it.draggable || it.isDroppedItem
                        || !ctx.Near(it.transform.position)
                        || it.GetComponentInParent<Player>() != null)
                    {
                        continue;
                    }

                    var e = ctx.Add(it.gameObject);
                    e["yaw"] =
                        StateDump.R(b.transform.rotation.eulerAngles.y, 1f);
                }
            });

            StateDump.AddSection("leaves", ctx =>
            {
                // Every open door's leaf, at its angle from shut (the doors
                // section has only whether it's drawn open).
                foreach (var d in StateDump.All<Door>())
                {
                    if (!ctx.Near(d.transform.position) || !d.opened
                        || d.destroyed || d.body == null)
                    {
                        continue;
                    }

                    ctx.Add(d.gameObject)["leaf"] =
                        StateDump.R(DoorLeaf.Signed(d), 1f);
                }
            });
        }

        private static string Fmt1(Vector3 p) =>
            FormattableString.Invariant($"{p.x:0.0} {p.z:0.0}");
        private static string Ref(Component c) =>
            ObjRef.Of(c.gameObject).ToString();
        private static Vector3 Dir(float deg) =>
            Quaternion.Euler(0f, deg, 0f) * Vector3.right;

        /// <summary>
        /// An Item with a rigidbody that the physics can move, not a character,
        /// door, window or dropped item.
        /// </summary>
        private static bool Movable(Rigidbody b, out Item it)
        {
            it = null;
            if (b == null || b.isKinematic
                || b.constraints == RigidbodyConstraints.FreezeAll)
            {
                return false;
            }

            if (b.GetComponent<CharBase>() != null
                || b.GetComponentInParent<Door>() != null
                || b.GetComponentInParent<Window>() != null)
            {
                return false;
            }

            it = b.GetComponent<Item>();
            if (it == null || it.GetComponentInParent<Player>() != null
                || it.isDroppedItem)
            {
                return false;
            }

            return Solid(b);
        }

        /// <summary>
        /// The body has a collider the player's body bumps into (not a trigger,
        /// on a layer that collides with the player's), switched on or off.
        /// </summary>
        private static bool Solid(Rigidbody b)
        {
            int pl = 0;
            if (P != null)
            {
                pl = P.gameObject.layer;
            }

            foreach (var c in b.GetComponentsInChildren<Collider>(true))
            {
                if (c.GetComponentInParent<Rigidbody>(true) == b
                    && !c.isTrigger
                    && !Physics.GetIgnoreLayerCollision(pl, c.gameObject.layer))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every body near the centre that a player's body can move, whatever
        /// it is.
        /// </summary>
        private static IEnumerable<Rigidbody> Bodies(float radius) =>
            Object.FindObjectsOfType<Rigidbody>().Where(
                b => b != null && !b.isKinematic
                    && b.constraints != RigidbodyConstraints.FreezeAll
                    && StateDump.Flat(b.position - Centre) < radius
                    && b.GetComponentInParent<Player>() == null && Solid(b));

        private static IEnumerator List(string[] a, Result r)
        {
            float radius = 400f;
            if (a.Length > 0 && a[0] != "new")
            {
                radius = F(a[0]);
            }

            var things = new List<Item>();
            var unwatched = new List<string>();
            foreach (var b in Bodies(radius)
                .OrderBy(b => Math.Round(StateDump.Flat(b.position - Centre)))
                .ThenBy(b => ObjRef.Of(b.gameObject)
                    .ToString(), StringComparer.Ordinal))
            {
                string what;
                if (b.GetComponent<CharBase>() != null)
                {
                    what = "character";
                }
                else if (b.GetComponentInParent<Door>() is Door d)
                {
                    string name = d.name;
                    string state;
                    var coop = Coop.Current;
                    if (coop != null && coop.Pushes.Watches(d))
                    {
                        state = "watched";
                    }
                    else if (d.opened)
                    {
                        state = "NOT WATCHED";
                    }
                    else
                    {
                        state = "shut";
                    }

                    what = $"door {name} leaf, {state}";
                }
                else if (b.GetComponentInParent<Window>() != null)
                {
                    what = "window";
                }
                else if (Movable(b, out var it))
                {
                    string w = "no DragSync";
                    if (DragSync.Instance != null)
                    {
                        w = DragSync.Instance.WatchInfo(b);
                    }

                    string text = "loose";
                    if (it.draggable)
                    {
                        text = "draggable";
                    }

                    what = $"{text}, {w}";
                    things.Add(it);
                    if (w == "not watched" || w == "no body")
                    {
                        unwatched.Add($"{it.name} {Ref(it)}");
                    }
                }
                else
                {
                    string bodyNote = "";
                    if (b.GetComponentInParent<Item>() != null)
                    {
                        bodyNote = " on the body, one above it";
                    }

                    what = $"NOT SHARED (no Item{bodyNote})";
                    unwatched.Add($"{b.name} {Ref(b)}");
                }

                Plugin.Log.LogInfo(
                    FormattableString.Invariant(
                            $"[push] {b.name} {Ref(b)} at {Fmt1(b.position)} ")
                        + FormattableString.Invariant(
                            $"mass {b.mass:0.##} drag {b.drag:0.##} ")
                        + FormattableString.Invariant(
                            $"layer {b.gameObject.layer}: {what}"));
            }

            if (_order.Count == 0 || a.Contains("new"))
            {
                _order.Clear();
                _order.AddRange(things.Select(Ref));
            }

            r.Info = string.Join(
                "; ",
                things.Select(
                    i => $"{_order.IndexOf(Ref(i))}={i.name} {Ref(i)} "
                        + $"{Fmt1(i.transform.position)}"));
            if (things.Count == 0)
            {
                r.Fail("nothing loose by the centre");
            }
            else if (unwatched.Count > 0)
            {
                r.Fail(
                    "not watched for pushes here: "
                        + $"{string.Join(", ", unwatched)}");
            }

            yield break;
        }

        private static Item ByIndex(string[] a, Result r)
        {
            int k = 0;
            if (a.Length > 0)
            {
                k = int.Parse(a[0], CultureInfo.InvariantCulture);
            }

            if (k < 0 || k >= _order.Count)
            {
                r.Fail($"no thing {k} (ps-list found {_order.Count})");
                return null;
            }

            var it = StateDump.All<Item>().FirstOrDefault(
                i => i != null && Ref(i) == _order[k]);
            if (it == null)
            {
                r.Fail($"thing {k} ({_order[k]}) not here");
            }

            return it;
        }

        /// <summary>
        /// How far the thing reaches out from its centre along the ground (its
        /// colliders' bounds).
        /// </summary>
        private static float Size(Item it)
        {
            float s = 20f;
            foreach (var c in it.GetComponentsInChildren<Collider>())
            {
                if (!c.isTrigger)
                {
                    s = Mathf.Max(
                        s, Mathf.Max(c.bounds.extents.x, c.bounds.extents.z));
                }
            }

            return s;
        }
    }
}
