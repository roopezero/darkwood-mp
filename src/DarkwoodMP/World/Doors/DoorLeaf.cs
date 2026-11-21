using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Doors
{
    /// <summary>
    /// A door opened while it's switched off in this game (another player
    /// opened it in an area our culling has off) swings open when its area is
    /// switched on again.
    ///
    /// Door.open makes the leaf's rigidbody free and pushes it with AddForce;
    /// an inactive rigidbody drops that force, so the door would be "opened"
    /// but drawn shut, and the next press (openClose) would close it for
    /// everyone. So the push is remembered (with where the opener was, which
    /// decides the side it swings to) and given again in Door.OnEnable, once
    /// the leaf takes part in the physics again.
    ///
    /// One lives as long as the game (ModHost.Leaves), so it works in a
    /// game played alone too, and a door opened before hosting from the
    /// pause menu is still waiting in the session; the doors waiting are
    /// forgotten when a session ends. Its static helpers read and turn a
    /// door's leaf.
    /// </summary>
    internal sealed class DoorLeaf
    {
        private readonly Dictionary<Door, Vector3> _pending =
            new Dictionary<Door, Vector3>();
        internal static readonly AccessTools.FieldRef<Door, Rigidbody> BodyRB =
            AccessTools.FieldRefAccess<Door, Rigidbody>("bodyRB");
        private static readonly AccessTools.FieldRef<Door, int> OpenForce =
            AccessTools.FieldRefAccess<Door, int>("openForce");

        /// <summary>
        /// How far the leaf is turned from shut, in degrees
        /// (Door.setCloseRotation leaves it at a local z of 90).
        /// </summary>
        internal static float Angle(Door d) => Mathf.Abs(Signed(d));

        /// <summary>
        /// True if the leaf is drawn open (more than 10 degrees from shut).
        /// </summary>
        internal static bool Turned(Door d) => LeafRules.Turned(Signed(d));

        /// <summary>
        /// What the leaf's rules know of a door, with rb as its leaf's
        /// Rigidbody (null for none): the only code that reads a door for
        /// them. Distance, Gliding and Watched are left for DoorPush.
        /// </summary>
        internal static LeafDoorFacts FactsOf(Door d, Rigidbody rb)
        {
            if (d == null)
            {
                return default;
            }

            var facts = new LeafDoorFacts
            {
                Exists = true,
                HasBody = d.body != null,
                Opened = d.opened,
                Destroyed = d.destroyed,
                Barricaded = d.barricaded,
            };

            if (d.body != null)
            {
                facts = facts with
                {
                    Active = d.body.gameObject.activeInHierarchy,
                };
            }

            if (rb != null)
            {
                facts = facts with
                {
                    HasRigidbody = true,
                    Kinematic = rb.isKinematic,
                };
            }

            return facts;
        }

        /// <summary>
        /// The door with its own leaf's Rigidbody (Door's bodyRB).
        /// </summary>
        internal static LeafDoorFacts FactsOf(Door d)
        {
            Rigidbody rb = null;
            if (d != null)
            {
                rb = BodyRB(d);
            }

            return FactsOf(d, rb);
        }

        /// <summary>
        /// The leaf's hinge, for how far it may turn.
        /// </summary>
        internal static HingeFacts HingeOf(Door d)
        {
            HingeJoint hinge = null;
            if (d != null && d.body != null)
            {
                hinge = d.body.GetComponent<HingeJoint>();
            }

            if (hinge == null)
            {
                return default;
            }

            return new HingeFacts
            {
                Present = true,
                UseLimits = hinge.useLimits,
                Min = hinge.limits.min,
                Max = hinge.limits.max,
            };
        }

        /// <summary>
        /// The leaf on the ground (Door.body is there), for how near a
        /// player is to it.
        /// </summary>
        internal static LeafShape ShapeOf(Door d)
        {
            var hinge = d.body.GetComponent<HingeJoint>();
            var shape = new LeafShape { Centre = Flat(d.body.position) };
            if (hinge != null)
            {
                shape = shape with
                {
                    HasHinge = true,
                    Hinge = Flat(hinge.connectedAnchor),
                };
            }

            return shape;
        }

        internal static Vec2 Flat(Vector3 v) => new Vec2(v.x, v.z);

        /// <summary>
        /// Door.open, with or without a session: the push is remembered for
        /// a leaf that's switched off here, then the session's DoorPush
        /// watches the leaf.
        /// </summary>
        internal void OnOpen(Door d, Vector3 position, Transform opener)
        {
            Opened(d, Doors.Opener(position, opener));
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Pushes.Opened(d);
            }
        }

        /// <summary>
        /// The session ended: the doors waiting for their push are
        /// forgotten.
        /// </summary>
        internal void Clear() => _pending.Clear();

        private void Opened(Door d, Vector3 opener)
        {
            if (!LeafRules.RemembersOpen(FactsOf(d)))
            {
                return;
            }

            _pending[d] = opener;
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] {d.name} opened while switched off here: it "
                        + "swings open when it's on again");
            }
        }

        internal void Closed(Door d) => _pending.Remove(d);

        internal void Enabled(Door d)
        {
            if (!_pending.TryGetValue(d, out var opener))
            {
                return;
            }

            _pending.Remove(d);
            if (LeafRules.SwingsOnEnable(FactsOf(d), Signed(d)))
            {
                d.StartCoroutine(Swing(d, opener));
            }
        }

        private static IEnumerator Swing(Door d, Vector3 opener)
        {
            yield return new WaitForFixedUpdate();
            if (!LeafRules.PushesOpen(FactsOf(d)))
            {
                yield break;
            }

            var rb = BodyRB(d);
            rb.AddForce(d.body.up * (Side(d, opener) * OpenForce(d)));
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] {d.name} switched on here: swung open");
            }
        }

        /// <summary>
        /// The way Door.open pushes the leaf (+1 along body.up, or -1), from
        /// where the opener stood.
        /// </summary>
        private static float Side(Door d, Vector3 opener)
        {
            var body = d.body;
            float yaw = body.rotation.eulerAngles.y;
            return LeafRules.Side(yaw, Flat(opener), Flat(body.position));
        }

        /// <summary>
        /// The leaf's signed angle from shut (DeltaAngle(local z, 90)), the
        /// value the mod saves as rot.
        /// </summary>
        internal static float Signed(Door d)
        {
            float z = LeafRules.ShutZ;
            if (d.body != null)
            {
                z = d.body.localRotation.eulerAngles.z;
            }

            return LeafRules.Signed(z);
        }

        /// <summary>
        /// How far the leaf turns from shut either way: its hinge's limit
        /// (110), or all the way round for a door whose limit the game switched
        /// off (Door.disableLimit). A leaf pushed past 105 used to be put at
        /// 105 in the other games (the save's range), 20 degrees and more from
        /// the pusher's, and refused from a client.
        /// </summary>
        internal static float MaxTurn(Door d) =>
            LeafRules.MaxTurn(HingeOf(d));

        /// <summary>
        /// Turn an open door's leaf to a signed angle from shut, about its
        /// hinge, and leave it still. Turning about world up by a adds a to the
        /// leaf's local z (Door.setCloseRotation turns it by 90 - z to shut
        /// it).
        /// The body is kinematic while it's turned, or the physics can put a
        /// free body's transform back.
        /// </summary>
        internal void SetLeaf(Door d, float signedAngle)
        {
            if (d == null || d.body == null)
            {
                return;
            }

            var rb = BodyRB(d);
            var hinge = d.body.GetComponent<HingeJoint>();
            if (rb == null || hinge == null)
            {
                return;
            }

            // it's where the others have it; the push it missed is no longer
            // wanted
            _pending.Remove(d);
            bool kinematic = rb.isKinematic;
            rb.isKinematic = true;
            Turn(d, signedAngle);
            rb.isKinematic = kinematic;
            if (!kinematic)
            {
                rb.angularVelocity = Vector3.zero;
                rb.velocity = Vector3.zero;
            }
        }

        /// <summary>
        /// An open door's leaf is saved as it's loaded. The game saves the
        /// leaf's local z angle (shut is 90) as rot, but Door.init turns a
        /// loaded open leaf by -rot from shut, which leaves it at 90 - rot:
        /// mirrored on every load (a leaf at 10 comes back at 80, nearly shut).
        /// A joiner loads the save the host just wrote, so its doors were
        /// always drawn mirrored from the host's. Saving 90 - z (signed) makes
        /// init put the leaf back where it was.
        /// The file keeps the game's format (a float rot within -105..105) and
        /// init, the only reader of rot, isn't patched, so a save written with
        /// the mod loads in the game without it with each leaf where it was,
        /// not mirrored; only a save the game writes by itself is mirrored on
        /// load, as it always was.
        /// </summary>
        internal static void Saving(Door.SaveState state, Door door)
        {
            if (door != null && door.body != null)
            {
                state.rot = LeafRules.SaveAngle(Signed(door));
            }
        }

        /// <summary>
        /// The leaf turned about its hinge to this signed angle from shut
        /// (within its limits), nothing else.
        /// </summary>
        internal static void Turn(Door d, float signedAngle)
        {
            HingeJoint hinge = null;
            if (d != null && d.body != null)
            {
                hinge = d.body.GetComponent<HingeJoint>();
            }

            if (hinge == null)
            {
                return;
            }

            float z = d.body.localRotation.eulerAngles.z;
            float by = LeafRules.TurnBy(z, signedAngle, MaxTurn(d));
            d.body.RotateAround(hinge.connectedAnchor, Vector3.up, by);
        }
    }
}
