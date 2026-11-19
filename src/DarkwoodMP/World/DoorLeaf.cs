using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A door opened while it's switched off in this game (another player opened it in an area our culling has
    /// off) swings open when its area is switched on again.
    ///
    /// Door.open makes the leaf's rigidbody free and pushes it with AddForce; an inactive rigidbody drops that
    /// force, so the door would be "opened" but drawn shut, and the next press (openClose) would close it for
    /// everyone. So the push is remembered (with where the opener was, which decides the side it swings to) and
    /// given again in Door.OnEnable, once the leaf takes part in the physics again.
    /// </summary>
    public static class DoorLeaf
    {
        private static readonly Dictionary<Door, Vector3> Pending = new Dictionary<Door, Vector3>();
        internal static readonly AccessTools.FieldRef<Door, Rigidbody> BodyRB = AccessTools.FieldRefAccess<Door, Rigidbody>("bodyRB");
        private static readonly AccessTools.FieldRef<Door, int> OpenForce = AccessTools.FieldRefAccess<Door, int>("openForce");

        /// <summary>How far the leaf is turned from shut, in degrees (Door.setCloseRotation leaves it at a local z of 90).</summary>
        public static float Angle(Door d) => Mathf.Abs(Signed(d));

        /// <summary>True if the leaf is drawn open (more than 10 degrees from shut).</summary>
        public static bool Turned(Door d) => Angle(d) > 10f;

        internal static void Opened(Door d, Vector3 opener)
        {
            if (d == null || d.body == null || !d.opened || d.body.gameObject.activeInHierarchy)
            {
                return;
            }
            Pending[d] = opener;
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] {d.name} opened while switched off here: it swings open when it's on again");
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
            if (d.opened && !d.destroyed && d.body != null && !Turned(d))
            {
                d.StartCoroutine(Swing(d, opener));
            }
        }

        private static IEnumerator Swing(Door d, Vector3 opener)
        {
            yield return new WaitForFixedUpdate();
            if (d == null || !d.opened || d.body == null)
            {
                yield break;
            }
            var rb = BodyRB(d);
            if (rb == null || rb.isKinematic)
            {
                yield break;
            }
            rb.AddForce(d.body.up * (Side(d, opener) * OpenForce(d)));
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] {d.name} switched on here: swung open");
            }
        }

        /// <summary>The way Door.open pushes the leaf (+1 along body.up, or -1), from where the opener stood.</summary>
        private static float Side(Door d, Vector3 opener)
        {
            var body = d.body;
            float y = body.rotation.eulerAngles.y;
            bool zMore = opener.z > body.position.z, xMore = opener.x > body.position.x;
            if (y > 179f && y < 181f)
            {
                if (zMore)
                {
                    return -1f;
                }
                return 1f;
            }
            if (y > -1f && y < 1f)
            {
                if (zMore)
                {
                    return 1f;
                }
                return -1f;
            }
            if (y > 89f && y < 91f)
            {
                if (xMore)
                {
                    return 1f;
                }
                return -1f;
            }
            if (y > 269f && y < 271f)
            {
                if (xMore)
                {
                    return -1f;
                }
                return 1f;
            }
            if (xMore)
            {
                return -1f;
            }
            return 1f;
        }

        public static void Clear() => Pending.Clear();

        /// <summary>The leaf's signed angle from shut (DeltaAngle(local z, 90)), the value the mod saves as rot.</summary>
        public static float Signed(Door d)
        {
            if (d.body == null)
            {
                return 0f;
            }
            return Mathf.DeltaAngle(d.body.localRotation.eulerAngles.z, 90f);
        }

        /// <summary>
        /// How far the leaf turns from shut either way: its hinge's limit (110), or all the way round for a door whose
        /// limit the game switched off (Door.disableLimit). A leaf pushed past 105 used to be put at 105 in the other
        /// games (the save's range), 20 degrees and more from the pusher's, and refused from a client.
        /// </summary>
        internal static float MaxTurn(Door d)
        {
            HingeJoint hinge = null;
            if (d != null && d.body != null)
            {
                hinge = d.body.GetComponent<HingeJoint>();
            }
            if (hinge == null || !hinge.useLimits)
            {
                return 180f;
            }
            return Mathf.Clamp(Mathf.Max(Mathf.Abs(hinge.limits.min), Mathf.Abs(hinge.limits.max)), 0f, 180f);
        }

        /// <summary>
        /// Turn an open door's leaf to a signed angle from shut, about its hinge, and leave it still. Turning about
        /// world up by a adds a to the leaf's local z (Door.setCloseRotation turns it by 90 - z to shut it).
        /// The body is kinematic while it's turned, or the physics can put a free body's transform back.
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
            Pending.Remove(d); // it's where the others have it; the push it missed is no longer wanted
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

        /// <summary>The leaf turned about its hinge to this signed angle from shut (within its limits), nothing else.</summary>
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
            signedAngle = Mathf.Clamp(signedAngle, -MaxTurn(d), MaxTurn(d));
            float by = Mathf.DeltaAngle(d.body.localRotation.eulerAngles.z, 90f - signedAngle);
            d.body.RotateAround(hinge.connectedAnchor, Vector3.up, by);
        }
    }

    /// <summary>
    /// An open door's leaf that a player walks into is shared. Door.open frees the leaf on its hinge, and from then
    /// on walking into it only turns it by physics: no game method is called (Door.close is only called by a press),
    /// so no WorldAction carries it. Each game watches the open doors around its own player; when a leaf that moved
    /// while our player was at it (within TouchDist of the leaf, from hinge to edge) comes to rest more than
    /// MinChange degrees from where it was last shared, its angle goes to the host (DoorLeaf). The host checks the
    /// pusher is within reach and the door is open in its game, turns its own leaf there and sends the angle to
    /// everyone, the pusher too, so if two players pushed the same leaf at once every game ends with the host's last
    /// word. The host sends where its leaf rests whatever turned it (its player, its enemies, physics); a client
    /// sends only its own player's push, and swings anything else back to where the leaf is shared. A leaf is
    /// followed once it swings or once it's more than MinChange from where it's shared, so a slow turn (a body
    /// leaning on it) is caught too.
    ///
    /// While the leaf is still swinging, its angle goes out ten times a second too (same message, same ordered
    /// channel, so the rest is always the last), and the other games turn their leaf towards the newest angle each
    /// frame (held kinematic meanwhile, let go at the rest): it swings in every game, rather than jumping when it
    /// stops. The host passes every angle back to its pusher too; a client skips those echoes of its own angles,
    /// which would turn its leaf back while its player is still pushing it.
    /// </summary>
    public static class DoorPush
    {
        private const float WatchRadius = 500f;
        private const float TouchDist = 120f;
        private const float MinChange = 4f;
        private const float RestTime = 0.4f;
        private const float MinInterval = 0.5f;
        private const float StreamInterval = 0.1f; // a swinging leaf's angle, sent this often
        private const float GlideRest = 0.25f;     // a leaf another game turned is let go this long after the last angle

        // leaves another game is turning, here: towards the newest angle heard
        private sealed class Glide
        {
            public float Target, Heard;
            public bool WasKinematic;
        }
        private static readonly Dictionary<Door, Glide> _glides = new Dictionary<Door, Glide>();
        // client: angles we sent, oldest first, until the host passes each back
        private static readonly Dictionary<Door, List<float>> _unechoed = new Dictionary<Door, List<float>>();

        private sealed class Watched
        {
            public Door D;
            public Rigidbody Rb;
            public float Shared;      // the angle last sent or got
            public bool Moving, Touched;
            public float StillSince = -1f, NextSend, NextStream;
        }

        private static Session _s;
        private static readonly Dictionary<Door, Watched> _watched = new Dictionary<Door, Watched>();
        private static readonly Collider[] _near = new Collider[256];
        private static float _nextLook;

        /// <summary>Map test: count a push of this door as our player's, wherever the player stands.</summary>
        internal static Door TestTouch;

        /// <summary>For the tests' log: this game watches the door's leaf for pushes.</summary>
        internal static bool Watches(Door d) => d != null && _watched.ContainsKey(d);

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
            if (!WorldActions.Sharing)
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
            if (Time.unscaledTime >= _nextLook)
            {
                _nextLook = Time.unscaledTime + 1f;
                Look(at);
            }
            List<Door> gone = null;
            foreach (var w in _watched.Values)
            {
                var d = w.D;
                if (d == null || !d.opened || d.destroyed || d.body == null || w.Rb == null || !d.body.gameObject.activeInHierarchy
                    || StateDump.Flat(d.body.position - at) > WatchRadius + 100f)
                {
                    (gone ?? (gone = new List<Door>())).Add(d);
                    continue;
                }
                if (w.Rb.isKinematic || _glides.ContainsKey(d))
                {
                    continue;
                }
                float now = DoorLeaf.Signed(d);
                bool swinging = Mathf.Abs(w.Rb.angularVelocity.y) > 0.2f;
                // Away from where it's shared, however slowly it got there (a body pressed gently against it turns
                // it slower than "swinging" and was never sent): followed until it rests, as a swing is.
                if (swinging || !w.Moving && Mathf.Abs(Mathf.DeltaAngle(now, w.Shared)) > MinChange)
                {
                    w.Moving = true;
                    if (!w.Touched && (Dist(d, at) < TouchDist || d == TestTouch))
                    {
                        w.Touched = true;
                    }
                }
                if (swinging)
                {
                    w.StillSince = -1f;
                    if ((_s.IsHost || w.Touched) && Time.unscaledTime >= w.NextStream)
                    {
                        w.NextStream = Time.unscaledTime + StreamInterval;
                        Post(w.D, now); // swinging: shown swinging in the other games
                    }
                    continue;
                }
                if (!w.Moving)
                {
                    continue;
                }
                if (w.StillSince < 0f)
                {
                    w.StillSince = Time.unscaledTime;
                    continue;
                }
                if (Time.unscaledTime - w.StillSince < RestTime || Time.unscaledTime < w.NextSend)
                {
                    continue;
                }
                w.Moving = false;
                if (Mathf.Abs(Mathf.DeltaAngle(now, w.Shared)) > MinChange)
                {
                    // The host's word on where it rests, whatever turned it; a client's only for its own player's
                    // push. Anything else turned a client's leaf (its copy of an enemy's bump, settling): it swings
                    // back to where it's shared, and the host's angle comes if the host's leaf turned too.
                    if (_s.IsHost || w.Touched)
                    {
                        Send(w, now);
                    }
                    else
                    {
                        if (WorldActions.Debug)
                        {
                            Plugin.Log.LogInfo($"[world] {d.name}'s leaf turned by itself here to {now:0}: back to {w.Shared:0}");
                        }
                        GlideTo(d, w.Shared);
                    }
                }
                w.Touched = false;
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
        /// This game opened a door (not a replay): watch it from now, from shut. Look finds open doors only once a
        /// second and takes their angle then as shared, so a leaf that swung open and came to rest within that
        /// second would never be sent, and each game's own swing from the open would leave the leaves apart. So
        /// where the opener's leaf comes to rest goes to the host, which passes it on, as a push.
        /// </summary>
        internal static void Opened(Door d)
        {
            if (!WorldActions.Sharing || WorldActions.Replaying || d == null || d.body == null || !d.opened || d.destroyed)
            {
                return;
            }
            var rb = DoorLeaf.BodyRB(d);
            if (rb == null)
            {
                return;
            }
            _watched[d] = new Watched { D = d, Rb = rb, Shared = 0f, Moving = true, Touched = true };
        }

        private static void Look(Vector3 at)
        {
            int n = Physics.OverlapSphereNonAlloc(at, WatchRadius, _near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = _near[i].attachedRigidbody;
                if (rb == null)
                {
                    continue;
                }
                var d = Door.getDoorScript(rb.transform);
                if (d == null || _watched.ContainsKey(d) || !d.opened || d.destroyed || d.body == null || DoorLeaf.BodyRB(d) != rb)
                {
                    continue;
                }
                _watched[d] = new Watched { D = d, Rb = rb, Shared = DoorLeaf.Signed(d) };
            }
        }

        /// <summary>Flat distance from p to the leaf, a segment from its hinge through the leaf's centre and as far again.</summary>
        private static float Dist(Door d, Vector3 p)
        {
            var hinge = d.body.GetComponent<HingeJoint>();
            Vector2 c = new Vector2(d.body.position.x, d.body.position.z), q = new Vector2(p.x, p.z);
            if (hinge == null)
            {
                return (q - c).magnitude;
            }
            Vector2 h = new Vector2(hinge.connectedAnchor.x, hinge.connectedAnchor.z), e = h + 2f * (c - h);
            var he = e - h;
            float t;
            if (he.sqrMagnitude < 1e-3f)
            {
                t = 0f;
            }
            else
            {
                t = Mathf.Clamp01(Vector2.Dot(q - h, he) / he.sqrMagnitude);
            }
            return (q - (h + t * he)).magnitude;
        }

        private static void Send(Watched w, float angle)
        {
            var r = ObjRef.Of(w.D.gameObject);
            if (!r.Valid)
            {
                return;
            }
            w.Shared = angle;
            w.NextSend = Time.unscaledTime + MinInterval;
            Post(w.D, angle);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] sent DoorLeaf {w.D.name} ({r}): pushed to {angle:0}");
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
                _unechoed[d] = sent = new List<float>();
            }
            if (sent.Count < 64)
            {
                sent.Add(angle);
            }
        }

        /// <summary>Client: this angle is the host passing back one we sent (see the class summary).</summary>
        private static bool OurEcho(Door d, float angle)
        {
            if (!_unechoed.TryGetValue(d, out var sent))
            {
                return false;
            }
            int i = sent.FindIndex(a => Mathf.Abs(a - angle) < 0.01f);
            if (i < 0)
            {
                _unechoed.Remove(d);
                return false;
            }
            sent.RemoveRange(0, i + 1);
            if (sent.Count == 0)
            {
                _unechoed.Remove(d);
            }
            return true;
        }

        /// <summary>Turn this game's leaf towards an angle another game sent, over the next frames.</summary>
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
            float k = 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime);
            List<Door> done = null;
            foreach (var kv in _glides)
            {
                var d = kv.Key;
                var g = kv.Value;
                if (d == null || d.body == null || !d.opened || d.destroyed) // shut: Door.close holds it
                {
                    (done ??= new List<Door>()).Add(d);
                    continue;
                }
                float now = DoorLeaf.Signed(d), off = Mathf.DeltaAngle(now, g.Target);
                float since = Time.unscaledTime - g.Heard;
                if (Mathf.Abs(off) < 0.5f && since > GlideRest || since > 1f)
                {
                    var rb = DoorLeaf.BodyRB(d);
                    if (rb != null)
                    {
                        rb.isKinematic = g.WasKinematic;
                    }
                    DoorLeaf.SetLeaf(d, g.Target);
                    if (_watched.TryGetValue(d, out var w))
                    {
                        w.Shared = g.Target;
                        w.Moving = false;
                        w.Touched = false;
                        w.StillSince = -1f;
                    }
                    (done ??= new List<Door>()).Add(d);
                    continue;
                }
                DoorLeaf.Turn(d, now + off * k);
            }
            if (done != null)
            {
                foreach (var d in done)
                {
                    _glides.Remove(d);
                }
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
            r.Write(pw);
            pw.W.Write(angle);
            return pw;
        }

        private static void OnLeaf(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld)
            {
                return;
            }
            if (_s.IsClient && from != _s.HostId)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            float angle = r.R.ReadSingle();
            if (float.IsNaN(angle) || float.IsInfinity(angle))
            {
                return;
            }
            var d = ObjRef.Resolve<Door>(obj);
            if (d == null || d.body == null)
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[world] got DoorLeaf {obj}: not found here");
                }
                return;
            }
            // Open and close are WorldActions on the same ordered channel; a leaf for a door that's shut (or broken,
            // or barricaded) here is late or wrong, and is left alone.
            if (!d.opened || d.destroyed || d.barricaded)
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[world] got DoorLeaf {d.name}: not open here, left as it is");
                }
                return;
            }
            if (_s.IsHost)
            {
                if (!Authority.Near(from, d.body.position, WorldActions.Reach) || Mathf.Abs(angle) > DoorLeaf.MaxTurn(d) + 5f)
                {
                    Plugin.Log.LogInfo($"[authority] {_s.NameOf(from)} pushed {d.name}'s leaf to {angle:0} from too far: put back");
                    _s.SendTo(from, Write(obj, DoorLeaf.Signed(d)), Channel.Control, reliable: true);
                    return;
                }
                _s.Broadcast(Write(obj, angle), Channel.Control, reliable: true); // the pusher too: the host's last word
            }
            if (_s.IsClient && OurEcho(d, angle))
            {
                return;
            }
            GlideTo(d, angle);
            if (_watched.TryGetValue(d, out var w))
            {
                w.Shared = angle;
                w.Moving = false;
                w.Touched = false;
                w.StillSince = -1f;
            }
        }
    }

    /// <summary>
    /// An open door's leaf is saved as it's loaded. The game saves the leaf's local z angle (shut is 90) as rot, but
    /// Door.init turns a loaded open leaf by -rot from shut, which leaves it at 90 - rot: mirrored on every load
    /// (a leaf at 10 comes back at 80, nearly shut). A joiner loads the save the host just wrote, so its doors were
    /// always drawn mirrored from the host's. Saving 90 - z (signed) makes init put the leaf back where it was.
    /// The file keeps the game's format (a float rot within -105..105) and init, the only reader of rot, isn't
    /// patched, so a save written with the mod loads in the game without it with each leaf where it was, not
    /// mirrored; only a save the game writes by itself is mirrored on load, as it always was.
    /// </summary>
    [HarmonyPatch(typeof(Door.SaveState), MethodType.Constructor, new[] { typeof(Door) })]
    static class DoorLeafSavePatch
    {
        static void Postfix(Door.SaveState __instance, Door door)
        {
            if (door != null && door.body != null)
            {
                __instance.rot = Mathf.Clamp(DoorLeaf.Signed(door), -105f, 105f);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.open))]
    static class DoorLeafOpenPatch
    {
        static void Postfix(Door __instance, Vector3 openerPosition, Transform openerTransform)
        {
            DoorLeaf.Opened(__instance, openerTransform != null ? openerTransform.position : openerPosition);
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
