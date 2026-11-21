using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using DarkwoodMP.Net;
using DarkwoodMP.World.Doors;
using UnityEngine;

namespace DarkwoodMP.World
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
    /// </summary>
    public static class DoorLeaf
    {
        private static readonly Dictionary<Door, Vector3> Pending =
            new Dictionary<Door, Vector3>();
        internal static readonly AccessTools.FieldRef<Door, Rigidbody> BodyRB =
            AccessTools.FieldRefAccess<Door, Rigidbody>("bodyRB");
        private static readonly AccessTools.FieldRef<Door, int> OpenForce =
            AccessTools.FieldRefAccess<Door, int>("openForce");

        /// <summary>
        /// How far the leaf is turned from shut, in degrees
        /// (Door.setCloseRotation leaves it at a local z of 90).
        /// </summary>
        public static float Angle(Door d) => Mathf.Abs(Signed(d));

        /// <summary>
        /// True if the leaf is drawn open (more than 10 degrees from shut).
        /// </summary>
        public static bool Turned(Door d) => LeafRules.Turned(Signed(d));

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

        internal static void Opened(Door d, Vector3 opener)
        {
            if (!LeafRules.RemembersOpen(FactsOf(d)))
            {
                return;
            }

            Pending[d] = opener;
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] {d.name} opened while switched off here: it "
                        + "swings open when it's on again");
            }
        }

        internal static void Closed(Door d) => Pending.Remove(d);

        internal static void Enabled(Door d)
        {
            if (!Pending.TryGetValue(d, out var opener))
            {
                return;
            }

            Pending.Remove(d);
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

        public static void Clear() => Pending.Clear();

        /// <summary>
        /// The leaf's signed angle from shut (DeltaAngle(local z, 90)), the
        /// value the mod saves as rot.
        /// </summary>
        public static float Signed(Door d)
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
        internal static void SetLeaf(Door d, float signedAngle)
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
            Pending.Remove(d);
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

    /// <summary>
    /// An open door's leaf that a player walks into is shared. Door.open frees
    /// the leaf on its hinge, and from then on walking into it only turns it by
    /// physics: no game method is called (Door.close is only called by a
    /// press), so no WorldAction carries it. Each game watches the open doors
    /// around its own player; when a leaf that moved while our player was at it
    /// (within TouchDist of the leaf, from hinge to edge) comes to rest more
    /// than MinChange degrees from where it was last shared, its angle goes to
    /// the host (DoorLeaf). The host checks the pusher is within reach and the
    /// door is open in its game, turns its own leaf there and sends the angle
    /// to everyone, the pusher too, so if two players pushed the same leaf at
    /// once every game ends with the host's last word. The host sends where its
    /// leaf rests whatever turned it (its player, its enemies, physics); a
    /// client sends only its own player's push, and swings anything else back
    /// to where the leaf is shared. A leaf is followed once it swings or once
    /// it's more than MinChange from where it's shared, so a slow turn (a body
    /// leaning on it) is caught too.
    ///
    /// While the leaf is still swinging, its angle goes out ten times a second
    /// too (same message, same ordered channel, so the rest is always the
    /// last), and the other games turn their leaf towards the newest angle each
    /// frame (held kinematic meanwhile, let go at the rest): it swings in every
    /// game, rather than jumping when it stops. The host passes every angle
    /// back to its pusher too; a client skips those echoes of its own angles,
    /// which would turn its leaf back while its player is still pushing it.
    /// </summary>
    public static class DoorPush
    {
        // leaves another game is turning, here: towards the newest angle heard
        private sealed class Glide
        {
            public float Target, Heard;
            public bool WasKinematic;
        }

        private static readonly Dictionary<Door, Glide> _glides =
            new Dictionary<Door, Glide>();
        // client: angles we sent, oldest first, until the host passes each back
        private static readonly Dictionary<Door, LeafEchoes> _unechoed =
            new Dictionary<Door, LeafEchoes>();

        private sealed class Watched
        {
            public Door D;
            public Rigidbody Rb;
            public LeafWatch State;
        }

        private static Session _s;
        private static readonly Dictionary<Door, Watched> _watched =
            new Dictionary<Door, Watched>();
        private static readonly Collider[] _near = new Collider[256];
        private static float _nextLook;

        /// <summary>
        /// Map test: count a push of this door as our player's, wherever the
        /// player stands.
        /// </summary>
        internal static Door TestTouch;

        /// <summary>
        /// For the tests' log: this game watches the door's leaf for pushes.
        /// </summary>
        internal static bool Watches(Door d) =>
            d != null && _watched.ContainsKey(d);

        public static void Register(Session s)
        {
            _s = s;
            s.Register(MsgType.DoorLeaf, OnLeaf);
            s.OnSessionEnded += () =>
            {
                _watched.Clear();
                DoorLeaf.Clear();
                _unechoed.Clear();
                EndGlides();
            };
        }

        public static void Update()
        {
            if (!Coop.Guards.Sharing)
            {
                if (_watched.Count > 0)
                {
                    _watched.Clear();
                }

                EndGlides();
                return;
            }

            if (_glides.Count > 0)
            {
                Glides();
            }

            var player = Player.Instance;
            if (player == null)
            {
                return;
            }

            var at = player.transform.position;
            var now = new Seconds(Time.unscaledTime);
            if (LeafWatchRules.DueToLook(now, new Seconds(_nextLook)))
            {
                _nextLook = now.Value + LeafWatchRules.LookInterval.Value;
                Look(at);
            }

            List<Door> gone = null;
            foreach (var w in _watched.Values)
            {
                var d = w.D;
                var facts = FactsOf(w, at);
                if (LeafWatchRules.Unwatches(facts))
                {
                    if (gone == null)
                    {
                        gone = new List<Door>();
                    }

                    gone.Add(d);
                    continue;
                }

                if (LeafWatchRules.Waits(facts))
                {
                    continue;
                }

                Step(w, FrameOf(w, at));
            }

            if (gone != null)
            {
                foreach (var d in gone)
                {
                    _watched.Remove(d);
                }
            }
        }

        /// <summary>
        /// What the rules know of a watched door: its leaf's Rigidbody is
        /// the one kept when the watch began.
        /// </summary>
        private static LeafDoorFacts FactsOf(Watched w, Vector3 at)
        {
            var d = w.D;
            var facts = DoorLeaf.FactsOf(d, w.Rb);
            if (facts.HasBody)
            {
                facts = facts with
                {
                    Distance = StateDump.Flat(d.body.position - at),
                };
            }

            if (facts.Exists)
            {
                facts = facts with { Gliding = _glides.ContainsKey(d) };
            }

            return facts;
        }

        /// <summary>
        /// One frame of a watched leaf that is there and free. It's taken
        /// as named here; Step asks ObjRef only when something is sent.
        /// </summary>
        private static LeafFrame FrameOf(Watched w, Vector3 at)
        {
            var d = w.D;
            return new LeafFrame
            {
                Watch = w.State,
                Now = new Seconds(Time.unscaledTime),
                Angle = DoorLeaf.Signed(d),
                Spin = w.Rb.angularVelocity.y,
                Host = _s.IsHost,
                Shape = DoorLeaf.ShapeOf(d),
                Player = DoorLeaf.Flat(at),
                TestTouch = d == TestTouch,
                Named = true,
            };
        }

        /// <summary>
        /// Do what the rules decided for a watched leaf this frame. A leaf
        /// whose name the other games don't know isn't sent: the frame is
        /// decided again as unnamed.
        /// </summary>
        private static void Step(Watched w, LeafFrame frame)
        {
            var step = LeafWatchRules.Step(frame);
            bool sends = step.Move == LeafMove.Stream
                || step.Move == LeafMove.Send;
            if (sends && !ObjRef.Of(w.D.gameObject).Valid)
            {
                step = LeafWatchRules.Step(frame with { Named = false });
            }

            w.State = step.Next;
            var d = w.D;
            switch (step.Move)
            {
                case LeafMove.Stream:
                    // swinging: shown swinging in the other games
                    Post(d, step.Angle);
                    break;
                case LeafMove.Send:
                    Send(d, step.Angle);
                    break;
                case LeafMove.SwingBack:
                    // Anything but our player turned a client's leaf (its
                    // copy of an enemy's bump, settling): it swings back to
                    // where it's shared, and the host's angle comes if the
                    // host's leaf turned too.
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] {d.name}'s leaf turned by itself "
                                + $"here to {frame.Angle:0}: back to "
                                + $"{step.Angle:0}");
                    }

                    GlideTo(d, step.Angle);
                    break;
            }
        }

        /// <summary>
        /// This game opened a door (not a replay): watch it from now, from
        /// shut. Look finds open doors only once a second and takes their angle
        /// then as shared, so a leaf that swung open and came to rest within
        /// that second would never be sent, and each game's own swing from the
        /// open would leave the leaves apart. So where the opener's leaf comes
        /// to rest goes to the host, which passes it on, as a push.
        /// </summary>
        internal static void Opened(Door d)
        {
            var open = new LeafOpenFacts
            {
                Sharing = Coop.Guards.Sharing,
                Replaying = Coop.Guards.Replaying,
                Door = DoorLeaf.FactsOf(d),
            };

            if (!LeafWatchRules.WatchesOpened(open))
            {
                return;
            }

            _watched[d] = new Watched
            {
                D = d,
                Rb = DoorLeaf.BodyRB(d),
                State = LeafWatchRules.FromOpen(),
            };
        }

        private static void Look(Vector3 at)
        {
            int n = Physics.OverlapSphereNonAlloc(
                at,
                LeafWatchRules.WatchRadius,
                _near,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = _near[i].attachedRigidbody;
                if (rb == null)
                {
                    continue;
                }

                var d = Door.getDoorScript(rb.transform);
                // the collider's Rigidbody counts only if it's the leaf's
                Rigidbody leaf = null;
                if (d != null && DoorLeaf.BodyRB(d) == rb)
                {
                    leaf = rb;
                }

                var facts = DoorLeaf.FactsOf(d, leaf);
                if (facts.Exists)
                {
                    facts = facts with { Watched = _watched.ContainsKey(d) };
                }

                if (!LeafWatchRules.Looks(facts))
                {
                    continue;
                }

                var state = LeafWatchRules.FromLook(DoorLeaf.Signed(d));
                _watched[d] = new Watched { D = d, Rb = rb, State = state };
            }
        }

        private static void Send(Door d, float angle)
        {
            Post(d, angle);
            if (WorldActions.Debug)
            {
                var r = ObjRef.Of(d.gameObject);
                Plugin.Log.LogInfo(
                    $"[world] sent DoorLeaf {d.name} ({r}): pushed to "
                        + $"{angle:0}");
            }
        }

        private static void Post(Door d, float angle)
        {
            var r = ObjRef.Of(d.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var pw = Write(r, angle);
            if (_s.IsHost)
            {
                _s.Broadcast(pw, Channel.Control, reliable: true);
                return;
            }

            _s.SendToHost(pw, Channel.Control, reliable: true);
            if (!_unechoed.TryGetValue(d, out var sent))
            {
                _unechoed[d] = sent = new LeafEchoes();
            }

            sent.Sent(angle);
        }

        /// <summary>
        /// Client: this angle is the host passing back one we sent (see the
        /// class summary).
        /// </summary>
        private static bool OurEcho(Door d, float angle)
        {
            if (!_unechoed.TryGetValue(d, out var sent))
            {
                return false;
            }

            bool echo = sent.IsEcho(angle);
            if (sent.Empty)
            {
                _unechoed.Remove(d);
            }

            return echo;
        }

        /// <summary>
        /// Turn this game's leaf towards an angle another game sent, over the
        /// next frames.
        /// </summary>
        private static void GlideTo(Door d, float angle)
        {
            var rb = DoorLeaf.BodyRB(d);
            if (rb == null)
            {
                return;
            }

            if (!_glides.TryGetValue(d, out var g))
            {
                _glides[d] = g = new Glide { WasKinematic = rb.isKinematic };
                rb.isKinematic = true; // the physics doesn't fight the turn
            }

            g.Target = angle;
            g.Heard = Time.unscaledTime;
        }

        private static void Glides()
        {
            List<Door> done = null;
            foreach (var kv in _glides)
            {
                var d = kv.Key;
                var g = kv.Value;
                var step = LeafGlideRules.Glide(GlideOf(d, g));
                if (step.Outcome == GlideOutcome.Turn)
                {
                    DoorLeaf.Turn(d, step.Angle);
                    continue;
                }

                if (step.Outcome == GlideOutcome.Settle)
                {
                    var rb = DoorLeaf.BodyRB(d);
                    if (rb != null)
                    {
                        rb.isKinematic = g.WasKinematic;
                    }

                    DoorLeaf.SetLeaf(d, step.Angle);
                    Heard(d, step.Angle);
                }

                if (done == null)
                {
                    done = new List<Door>();
                }

                done.Add(d);
            }

            if (done != null)
            {
                foreach (var d in done)
                {
                    _glides.Remove(d);
                }
            }
        }

        /// <summary>
        /// One frame of a leaf this game turns to another game's angle.
        /// </summary>
        private static GlideFacts GlideOf(Door d, Glide g)
        {
            var facts = new GlideFacts
            {
                Door = DoorLeaf.FactsOf(d),
                Target = g.Target,
                Heard = new Seconds(g.Heard),
                Now = new Seconds(Time.unscaledTime),
                Delta = new Seconds(Time.unscaledDeltaTime),
            };

            if (facts.Door.HasBody)
            {
                facts = facts with { Angle = DoorLeaf.Signed(d) };
            }

            return facts;
        }

        /// <summary>
        /// The leaf is where another game has it: watched from there.
        /// </summary>
        private static void Heard(Door d, float angle)
        {
            if (_watched.TryGetValue(d, out var w))
            {
                w.State = LeafWatchRules.Heard(w.State, angle);
            }
        }

        private static void EndGlides()
        {
            foreach (var kv in _glides)
            {
                Rigidbody rb = null;
                if (kv.Key != null)
                {
                    rb = DoorLeaf.BodyRB(kv.Key);
                }

                if (rb != null && kv.Key.opened && !kv.Key.destroyed)
                {
                    rb.isKinematic = kv.Value.WasKinematic;
                }
            }

            _glides.Clear();
        }

        private static PacketWriter Write(ObjRef r, float angle)
        {
            var pw = _s.Begin(MsgType.DoorLeaf);
            DoorLeafMessage.Write(pw, ObjRef.NameOf(r), angle);
            return pw;
        }

        private static void OnLeaf(ulong from, PacketReader r)
        {
            var obj = ObjRef.RefOf(DoorLeafMessage.Read(r, out float angle));
            var d = ObjRef.Resolve<Door>(obj);
            switch (LeafGlideRules.Hear(HeardOf(from, d, angle)))
            {
                case LeafAnswer.NotFound:
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] got DoorLeaf {obj}: not found here");
                    }

                    return;
                case LeafAnswer.NotOpen:
                    // Open and close are WorldActions on the same ordered
                    // channel; a leaf for a door that's shut (or broken, or
                    // barricaded) here is late or wrong, and is left alone.
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] got DoorLeaf {d.name}: not open here, "
                                + "left as it is");
                    }

                    return;
                case LeafAnswer.PutBack:
                    Plugin.Log.LogInfo(
                        $"[authority] {_s.NameOf(from)} pushed {d.name}'s "
                            + $"leaf to {angle:0} from too far: put back");
                    _s.SendTo(
                        from,
                        Write(obj, DoorLeaf.Signed(d)),
                        Channel.Control,
                        reliable: true);
                    return;
                case LeafAnswer.Relay:
                    // the pusher too: the host's last word
                    _s.Broadcast(
                        Write(obj, angle), Channel.Control, reliable: true);
                    break;
                case LeafAnswer.Take:
                    if (OurEcho(d, angle))
                    {
                        return;
                    }

                    break;
                default:
                    return;
            }

            GlideTo(d, angle);
            Heard(d, angle);
        }

        /// <summary>
        /// What this game knows as another game's leaf angle arrives.
        /// </summary>
        private static LeafHeardFacts HeardOf(ulong from, Door d, float angle)
        {
            var facts = new LeafHeardFacts
            {
                InWorld = WorldShare.InWorld,
                Host = _s.IsHost,
                Client = _s.IsClient,
                FromHost = from == _s.HostId,
                Angle = angle,
                Door = DoorLeaf.FactsOf(d),
            };

            if (_s.IsHost && facts.Door.HasBody)
            {
                var near = Coop.Current.Authority.NearOf(
                    from, d.body.position);
                facts = facts with
                {
                    Pusher = near,
                    MaxTurn = DoorLeaf.MaxTurn(d),
                };
            }

            return facts;
        }
    }

    /// <summary>
    /// An open door's leaf is saved as it's loaded. The game saves the leaf's
    /// local z angle (shut is 90) as rot, but Door.init turns a loaded open
    /// leaf by -rot from shut, which leaves it at 90 - rot: mirrored on every
    /// load (a leaf at 10 comes back at 80, nearly shut). A joiner loads the
    /// save the host just wrote, so its doors were always drawn mirrored from
    /// the host's. Saving 90 - z (signed) makes init put the leaf back where it
    /// was.
    /// The file keeps the game's format (a float rot within -105..105) and
    /// init, the only reader of rot, isn't patched, so a save written with the
    /// mod loads in the game without it with each leaf where it was, not
    /// mirrored; only a save the game writes by itself is mirrored on load, as
    /// it always was.
    /// </summary>
    [HarmonyPatch(
        typeof(Door.SaveState), MethodType.Constructor, new[] { typeof(Door) })]
    static class DoorLeafSavePatch
    {
        static void Postfix(Door.SaveState __instance, Door door)
        {
            if (door != null && door.body != null)
            {
                __instance.rot = LeafRules.SaveAngle(DoorLeaf.Signed(door));
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.open))]
    static class DoorLeafOpenPatch
    {
        static void Postfix(
            Door __instance, Vector3 openerPosition, Transform openerTransform)
        {
            Vector3 opener;
            if (openerTransform != null)
            {
                opener = openerTransform.position;
            }
            else
            {
                opener = openerPosition;
            }

            DoorLeaf.Opened(__instance, opener);
            DoorPush.Opened(__instance);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.close))]
    static class DoorLeafClosePatch
    {
        static void Postfix(Door __instance) => DoorLeaf.Closed(__instance);
    }

    [HarmonyPatch(typeof(Door), "OnEnable")]
    static class DoorLeafEnablePatch
    {
        static void Postfix(Door __instance) => DoorLeaf.Enabled(__instance);
    }
}
