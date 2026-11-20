using System.Collections.Generic;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Furniture and other objects a player drags (Item.startDragging: a hinge joint to the player's body), shared.
    ///
    /// While the local player drags something, its position goes out ten times a second (unreliably), and where
    /// it ends up goes out reliably when the drag ends, and again once it has settled. The other games hold the
    /// object still (kinematic) and slide it to each position they get, then let its physics go again when the
    /// drag is over. A client's drags go to the host, which moves the object too and passes them on.
    ///
    /// Other players never shove loose things in a game that isn't theirs: on the host their stand-ins for the AI
    /// (NetPlayerProxy) don't collide with them, and the puppets in every game have no solid colliders. So a loose
    /// thing only moves in a game because that game's player moved it, or because the world did.
    ///
    /// Pushed objects too: every loose object (an Item with a rigidbody: furniture, placed objects, dropped items) is
    /// noted where it is known to be in every game (where it was when sharing began or it was first seen, then
    /// wherever it was last sent or heard to be). Ten times a second each is compared with that; one that's more
    /// than half a unit or two degrees away and at rest again is settled, so a push between two checks is caught
    /// too. While it's still sliding, where it is goes out too, unreliably, as a drag's moves do, so the other games
    /// show it sliding rather than jumping to its rest; they glide the last bit to where it came to rest. The host sends where it is as a drag's end (ObjMove, last), whatever moved it (its player, an explosion,
    /// an enemy, physics settling). A client sends only what its own player was at while it was away from there,
    /// or hit in the seconds before (a shot pushes from afar; the host checks it as it checks a drag: the mover
    /// within reach of the object and of where it went), and puts anything else back, however far it went
    /// (settling, a nudge, its copy of an explosion): if the host's copy moved too, the host's word on it comes as
    /// an ObjMove. So the last word is the host's resting position. The host passes each rest back to its mover
    /// too; a client skips that echo of its own word (OurEcho), which would put the thing back under its player
    /// if they had pushed it on in the meantime.
    /// An object put where another game said is left asleep, so it doesn't drift from there by itself.
    ///
    /// The object keeps the ObjRef it had before it moved (ObjRef.Pin), so the ones named by where they stand
    /// are still found after they've been moved.
    ///
    /// One player drags a thing at a time, and the host decides who (DragClaim, DragOwner). A client that takes hold
    /// of something asks the host for it (a Request) and sends its positions only once the host agrees; the host
    /// refuses it while someone else is dragging it, and the client then lets go at once and is shown where the
    /// thing is ("NAME is moving it"). The host's own player takes hold of it the same way, against the same list.
    /// The host takes a drag's moves only from its owner (anyone else is sent where the thing is), and frees the
    /// drag when its owner lets go, stops sending for StaleAfter seconds, leaves, or drags it out of reach. Every
    /// game is told who owns each drag, so a player who tries to take hold of something someone else is moving is
    /// stopped before the game starts the drag (Item.startDragging).
    /// </summary>
    public sealed class DragSync
    {
        private const float SendInterval = 0.1f;
        private const float StaleAfter = 3f; // a mover that stopped sending (left, lost the last packet): let go
        private const float GlideFor = 0.6f;  // at most this long to glide to where another game's move came to rest
        private const float GlideMax = 300f;  // further than this, it's put there at once

        private readonly Session _session;

        // the local drag
        private Item _dragging;
        private ObjRef _dragRef;
        private float _nextSend;
        private Item _settling;       // the object just let go of, sent again once it's come to rest
        private ObjRef _settlingRef;
        private float _settleAt;
        private bool _claimed;        // client: the host agreed that our player drags _dragging (we send its moves)
        // client: where the host (or the player who owns the drag) last said the thing we're holding is, for letting go
        private readonly Dictionary<GameObject, (Vector3 pos, Quaternion rot)> _hostAt = new Dictionary<GameObject, (Vector3, Quaternion)>();
        // client: where we said a thing came to rest, oldest first, until the host passes each back to us (its echo)
        private readonly Dictionary<GameObject, List<Vector3>> _unechoed = new Dictionary<GameObject, List<Vector3>>();

        // who is dragging what (by ObjRef): decided on the host, as the host said on a client
        private sealed class Owner
        {
            public ulong Id;
            public float Heard;
            public ObjRef Ref;
        }
        private readonly Dictionary<string, Owner> _owners = new Dictionary<string, Owner>();
        private readonly Dictionary<string, float> _corrected = new Dictionary<string, float>(); // host: a non-owner last sent where it is
        private float _nextFree;
        /// <summary>Drags of ours cancelled because someone else had it (for the tests' log).</summary>
        public static int Cancelled;

        // others' drags, shown here
        private sealed class Moving
        {
            public Transform T;
            public Rigidbody Body;
            public bool WasKinematic;
            public Vector3 Pos;
            public Quaternion Rot;
            public bool Last;
            public float Heard;
        }
        private readonly Dictionary<GameObject, Moving> _moving = new Dictionary<GameObject, Moving>();

        // loose objects and where they're known to be, checked until they're back at rest wherever they are
        private const float PushWatchRadius = 600f;
        private const float PushWatchInterval = 0.1f; // also how often a thing still sliding is sent
        private const float PushLookInterval = 0.25f;  // new loose things noted around the players
        private float _nextLook;
        private const float PushMinMove = 0.5f;    // world units (a crate is about 50 across)
        private const float PushMinTurn = 2f;      // degrees
        private const float PushTouch = 120f;      // a client's player this close to a thing that moved moved it
        private const float PushGiveUpAfter = 10f; // still moving after this: sent as it is
        private const float PushHitFor = 3f;       // a thing our player hit moves by that hit for this long
        private sealed class Loose
        {
            public Item Item;
            public Rigidbody Body;
            public Collider Col;
            public ObjRef Ref;
            public Vector3 Pos;      // where it's known to be in every game: as loaded, or as last sent or heard
            public Quaternion Rot;
            public float Since = -1f; // when it was first seen away from there (-1: it isn't)
            public bool Touched;      // a client's own player was at it while it was away from there
            public float HitAt = -1f; // when our player last hit it (a shot pushes from afar)
        }
        private readonly Dictionary<Rigidbody, Loose> _loose = new Dictionary<Rigidbody, Loose>();
        private bool _looseFound; // every loose object in the world so far has been noted
        private readonly Collider[] _near = new Collider[512];
        private float _nextPushWatch;
        /// <summary>Pushed objects this game sent (for the tests' log).</summary>
        public static int PushesSent;

        public static DragSync Instance { get; private set; }

        public DragSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.ObjMove, OnMove);
            session.Register(MsgType.DragClaim, OnClaim);
            session.Register(MsgType.DragOwner, OnDragOwner);
            session.OnPeerDisconnected += FreeOwnedBy;
            session.OnSessionEnded += () =>
            {
                foreach (var m in _moving.Values)
                {
                    Release(m);
                }
                _moving.Clear();
                ForgetLoose();
                _dragging = null;
                _settling = null;
                _owners.Clear();
                _corrected.Clear();
                _hostAt.Clear();
                _unechoed.Clear();
                _claimed = false;
            };
        }

        private void ForgetLoose()
        {
            _loose.Clear();
            _looseFound = false;
        }

        public void Update()
        {
            ShowOthers();
            if (!WorldActions.Sharing)
            {
                _dragging = null;
                _claimed = false;
                if (_loose.Count > 0 || _looseFound)
                {
                    ForgetLoose();
                }
                if (_owners.Count > 0)
                {
                    _owners.Clear();
                }
                return;
            }
            var player = Player.Instance;
            Item now = null;
            if (player != null)
            {
                now = player.itemBeingDragged;
            }

            if (now != _dragging)
            {
                if (_dragging != null) // let go: where it stopped, now and once it has settled
                {
                    Send(_dragging, _dragRef, last: true);
                    _settling = _dragging;
                    _settlingRef = _dragRef;
                    _settleAt = Time.unscaledTime + 1.5f;
                    LetGo(_dragRef);
                }
                _dragging = now;
                _claimed = false;
                if (now != null)
                {
                    StartDrag(now);
                }
            }
            if (_dragging != null && _session.IsHost && _owners.TryGetValue(_dragRef.ToString(), out var mine) && mine.Id == _session.LocalId)
            {
                mine.Heard = Time.unscaledTime; // the host's own drag never goes stale
            }
            if (Time.unscaledTime >= _nextFree)
            {
                _nextFree = Time.unscaledTime + 1f;
                FreeStale();
            }

            if (_dragging != null && _dragRef.Valid && (_session.IsHost || _claimed) && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + SendInterval;
                Send(_dragging, _dragRef, last: false);
            }
            if (_settling != null && Time.unscaledTime >= _settleAt)
            {
                Send(_settling, _settlingRef, last: true);
                _settling = null;
            }
            if (Time.unscaledTime >= _nextPushWatch)
            {
                _nextPushWatch = Time.unscaledTime + PushWatchInterval;
                WatchPushes(player);
            }
        }

        private static bool AtRest(Rigidbody b) => b.IsSleeping() || (b.velocity.sqrMagnitude < 1f && b.angularVelocity.sqrMagnitude < 0.01f);

        /// <summary>
        /// Every loose object this game knows of is checked against where it's known to be in every game. One that's
        /// away from there by more than a little, once it's at rest again, is settled: the host sends where it is
        /// (whatever moved it, even in a moment between two checks); a client sends it only if its own player was at
        /// it, and otherwise puts its copy back, for the host's word on it (which comes as an ObjMove if it moved
        /// there too). So the host's resting position is the last word on everything.
        /// </summary>
        private void WatchPushes(Player player)
        {
            if (player == null)
            {
                return;
            }
            var me = player.transform.position;
            if (!_looseFound)
            {
                _looseFound = true;
                // The host notes the switched-off ones too (where the save put them): a part of the world switched on
                // later around a client's player settles there (a chair, a table shifting a few units), and that is
                // then sent to everyone, so a client whose copy is still switched off has them settled too. Noted
                // only once seen, they would have settled already and nothing would be sent, leaving that client's
                // copies a few units from everyone else's.
                foreach (var b in Object.FindObjectsOfType<Rigidbody>(_session.IsHost))
                {
                    Note(b, null);
                }
            }
            if (Time.unscaledTime >= _nextLook)
            {
                _nextLook = Time.unscaledTime + PushLookInterval;
                Look(me);
                if (_session.IsHost)
                {
                    foreach (var proxy in Combat.NetPlayerProxy.All.Values)
                    {
                        if (proxy != null && proxy.alive && !proxy.Away)
                        {
                            Look(proxy.transform.position);
                        }
                    }
                }
            }
            // A client away (spectating) has no player of its own to push anything: it takes the host's word on all.
            // Our pushes count wherever the host runs our area too (a separate location it placed, the shared dream):
            // only in an area that's ours alone does what our player knocks over stay ours.
            bool clientAway = _session.IsClient && !Locations.HostRunsHere();
            float nowT = Time.unscaledTime;
            List<Rigidbody> gone = null;
            foreach (var kv in _loose)
            {
                var p = kv.Value;
                if (p.Body == null || p.Item == null)
                {
                    if (gone == null)
                    {
                        gone = new List<Rigidbody>();
                    }
                    gone.Add(kv.Key);
                    continue;
                }
                var t = p.Item.transform;
                if (p.Body.isKinematic || p.Item == _dragging || p.Item == _settling || _moving.ContainsKey(p.Body.gameObject))
                {
                    Rebase(p); // where the drag or the other game puts it, which is sent or heard as that
                    continue;
                }
                float moved = (t.position - p.Pos).magnitude;
                if (moved < PushMinMove && Quaternion.Angle(t.rotation, p.Rot) < PushMinTurn)
                {
                    p.Since = -1f;
                    p.Touched = false;
                    continue;
                }
                if (p.Since < 0f)
                {
                    p.Since = nowT;
                }
                if (_session.IsClient && !clientAway && !p.Touched && (p.Col != null && p.Col.bounds.SqrDistance(me) < PushTouch * PushTouch
                    || p.HitAt >= 0f && nowT - p.HitAt < PushHitFor))
                {
                    p.Touched = true;
                }
                if (!AtRest(p.Body) && nowT - p.Since < PushGiveUpAfter)
                {
                    // Still sliding: where it is now goes out too (unreliably, as a drag's moves), so the other games
                    // show it sliding there instead of jumping to where it comes to rest. Only what this game has the
                    // word on: the host's, or a client's own player's push.
                    if (_session.IsHost || p.Touched)
                    {
                        Send(p.Item, p.Ref, last: false);
                    }
                    continue;
                }
                if (_session.IsClient && !p.Touched)
                {
                    // Not our player's doing (settling, a nudge, our copy of an explosion): the host decides where it
                    // is, so our copy goes back to where it was last known to be.
                    t.SetPositionAndRotation(p.Pos, p.Rot);
                    p.Body.position = p.Pos;
                    p.Body.rotation = p.Rot;
                    p.Body.velocity = Vector3.zero;
                    p.Body.angularVelocity = Vector3.zero;
                    p.Body.Sleep();
                    p.Since = -1f;
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo($"[world] {p.Item.name} ({p.Ref}) moved {moved:0.0} units by itself: put back");
                    }
                    continue;
                }
                Send(p.Item, p.Ref, last: true); // which notes it there
                PushesSent++;
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[world] pushed {p.Item.name} ({p.Ref}) {moved:0.0} units: sent where it is");
                }
            }
            if (gone != null)
            {
                foreach (var b in gone)
                {
                    _loose.Remove(b);
                }
            }
        }

        /// <summary>
        /// Our player hit this thing (Item.getHit). A shot pushes it from as far as the gun reaches, where the
        /// player isn't within PushTouch of it, and a client would put it back as not its player's doing (and the
        /// host's copy, which gets the hit but not its push, doesn't move). So for a few seconds a move of it counts
        /// as our player's push.
        /// </summary>
        public void Hit(Item item)
        {
            Rigidbody b = null;
            if (item != null)
            {
                b = item.GetComponent<Rigidbody>();
            }
            if (b == null)
            {
                return;
            }
            if (!_loose.ContainsKey(b))
            {
                Note(b, null);
            }
            if (_loose.TryGetValue(b, out var p))
            {
                p.HitAt = Time.unscaledTime;
            }
        }

        // Host: every loose object where the last snapshot for joiners has it (WorldShare.SaveSnapshot).
        private static readonly Dictionary<Rigidbody, (Vector3 pos, Quaternion rot)> _atSnapshot = new Dictionary<Rigidbody, (Vector3, Quaternion)>();
        /// <summary>When the last snapshot's positions were noted (Time.unscaledTime; -1: none). For the tests.</summary>
        public static float SnapshotAt = -1f;

        /// <summary>
        /// Host, in the frame the world is read for a joiner's snapshot: where every loose object is in it. Something
        /// still moving then (a dropped item sliding off, furniture settling) is saved mid-way, and the joiner's game
        /// loads it there and keeps it there; the host's rest position, if sent while the joiner was still loading,
        /// was dropped (ObjMove is only taken in the world). JoinerInWorld sends the difference.
        /// </summary>
        public static void NoteSnapshot()
        {
            _atSnapshot.Clear();
            foreach (var b in Object.FindObjectsOfType<Rigidbody>(true))
            {
                if (LooseItem(b) == null)
                {
                    continue;
                }
                ObjRef.Pin(b.gameObject); // named by where the snapshot has it, which is where the joiner finds it
                _atSnapshot[b] = (b.transform.position, b.transform.rotation);
            }
            SnapshotAt = Time.unscaledTime;
        }

        /// <summary>Host: the world offered to joiners isn't a snapshot (LiveWorld off: the last save), so nothing is known of it.</summary>
        public static void ForgetSnapshot()
        {
            _atSnapshot.Clear();
            SnapshotAt = -1f;
        }

        /// <summary>
        /// Host: a joiner is in the world it loaded (it asked for its character). Each loose object that's no longer
        /// where the snapshot had it, and is at rest, is sent to that joiner (ObjMove, last). One still moving is
        /// noted at its snapshot position, so it is sent to everyone once it's at rest (WatchPushes).
        /// </summary>
        public void JoinerInWorld(ulong who)
        {
            if (!_session.IsHost || _atSnapshot.Count == 0)
            {
                return;
            }
            int sent = 0, later = 0;
            foreach (var kv in _atSnapshot)
            {
                var b = kv.Key;
                if (b == null)
                {
                    continue;
                }
                var t = b.transform;
                if ((t.position - kv.Value.pos).magnitude < PushMinMove && Quaternion.Angle(t.rotation, kv.Value.rot) < PushMinTurn)
                {
                    continue;
                }
                var item = LooseItem(b);
                if (item == null || b.isKinematic || item == _dragging || _moving.ContainsKey(b.gameObject))
                {
                    continue; // a drag sends its own end
                }
                if (!AtRest(b))
                {
                    if (_loose.TryGetValue(b, out var p))
                    {
                        p.Pos = kv.Value.pos;
                        p.Rot = kv.Value.rot;
                        p.Since = -1f;
                        later++;
                    }
                    continue;
                }
                var r = ObjRef.Pin(b.gameObject);
                if (!r.Valid)
                {
                    continue;
                }
                _session.SendTo(who, Message(r, t.position, t.rotation, true), Channel.Control, reliable: true);
                sent++;
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[world] {item.name} ({r}) moved {(t.position - kv.Value.pos).magnitude:0.0} units since {_session.NameOf(who)}'s snapshot: sent where it is");
                }
            }
            if (sent > 0 || later > 0)
            {
                string moving = "";
                if (later > 0)
                {
                    moving = $", {later} still moving (sent once at rest)";
                }
                Plugin.Log.LogInfo($"{_session.NameOf(who)} is in the world: {sent} loose objects that moved since their snapshot sent to them{moving}");
            }
        }

        /// <summary>
        /// Host: notes every loose object under root at once (a separate location it starts running for a joiner).
        /// Objects are otherwise noted only within PushWatchRadius of a player, and one an enemy pushes far from
        /// everyone (a dog chasing about a location while its joiner is elsewhere in it) would move unseen and stay
        /// where the host's enemy left it in the host's game only.
        /// </summary>
        public void NoteAll(GameObject root)
        {
            if (root == null || !_session.IsHost)
            {
                return;
            }
            foreach (var b in root.GetComponentsInChildren<Rigidbody>(true))
            {
                Note(b, null);
            }
        }

        /// <summary>
        /// Host: an object the world made has just been given an id (HostIds) and announced where it stands now. It is
        /// noted there at once, so if it settles or is pushed later (a location loaded for a joiner, whose furniture
        /// only settles once it's switched on), the place it comes to rest is sent as a drag's end, like any push.
        /// </summary>
        public void Watch(GameObject go)
        {
            if (go == null || !_session.IsHost)
            {
                return;
            }
            Note(go.GetComponent<Rigidbody>(), null);
        }

        /// <summary>
        /// Client: our copy of an object the host gave an id to has just been tied to it. A loose one goes to where the
        /// host has it (the host decides where things are): each game's physics settles a fresh copy on its own, so the
        /// two can come to rest a few units apart, and nothing else would move ours. Left alone while it's being
        /// dragged, held still for another game's move, or kinematic (carried, fixed in place). True if it was moved.
        /// </summary>
        public bool PutAtHost(GameObject go, Vector3 pos)
        {
            if (go == null || !_session.IsClient)
            {
                return false;
            }
            var b = go.GetComponent<Rigidbody>();
            var item = LooseItem(b);
            if (item == null || b.isKinematic || item == _dragging || item == _settling || _moving.ContainsKey(go))
            {
                return false;
            }
            var t = go.transform;
            float d = (t.position - pos).magnitude;
            if (d < PushMinMove)
            {
                return false;
            }
            var r = ObjRef.Of(go);
            if (r.Valid && OtherOwner(r) != null)
            {
                return false;
            }
            t.position = pos;
            b.position = pos;
            b.velocity = Vector3.zero;
            b.angularVelocity = Vector3.zero;
            b.Sleep(); // where the host has it: not to drift off by itself here
            Rebase(go);
            Recell(go);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(System.FormattableString.Invariant($"[world] {go.name} ({r}) put where the host has it, {d:0.0} units from where it settled here"));
            }
            return true;
        }

        /// <summary>Notes the loose objects around center not noted yet (spawned, or come into the world since).</summary>
        private void Look(Vector3 center)
        {
            int n = Physics.OverlapSphereNonAlloc(center, PushWatchRadius, _near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var b = _near[i].attachedRigidbody;
                if (b != null && !_loose.ContainsKey(b))
                {
                    Note(b, _near[i]);
                }
            }
        }

        /// <summary>An Item with a rigidbody that can move, not a character, door or window.</summary>
        private static Item LooseItem(Rigidbody b)
        {
            if (b == null || b.constraints == RigidbodyConstraints.FreezeAll)
            {
                return null;
            }
            var item = b.GetComponent<Item>();
            if (item == null)
            {
                return null;
            }
            if (b.GetComponent<CharBase>() != null || b.GetComponentInParent<Door>() != null || b.GetComponentInParent<Window>() != null)
            {
                return null;
            }
            return item;
        }

        /// <summary>A loose object (an Item with a rigidbody that can move), noted where it is now.</summary>
        private void Note(Rigidbody b, Collider col)
        {
            if (b == null || _loose.ContainsKey(b))
            {
                return;
            }
            var item = LooseItem(b);
            if (item == null)
            {
                return;
            }
            var r = ObjRef.Pin(b.gameObject);
            if (!r.Valid)
            {
                return;
            }
            var p = new Loose { Item = item, Body = b, Ref = r };
            if (col != null)
            {
                p.Col = col;
            }
            else
            {
                p.Col = b.GetComponentInChildren<Collider>();
            }
            Rebase(p);
            _loose[b] = p;
        }

        /// <summary>For the tests' log: whether this body is watched for pushes here, and where it's known to be.</summary>
        internal string WatchInfo(Rigidbody b)
        {
            if (b == null)
            {
                return "no body";
            }
            if (!_loose.TryGetValue(b, out var p))
            {
                return "not watched";
            }
            string text = "";
            if (p.Since >= 0f)
            {
                text = ", away";
            }
            string text2 = "";
            if (p.Touched)
            {
                text2 = ", touched";
            }
            return System.FormattableString.Invariant($"watched, known at {p.Pos.x:0.0} {p.Pos.z:0.0}{text}{text2}");
        }

        private static void Rebase(Loose p)
        {
            var t = p.Item.transform;
            p.Pos = t.position;
            p.Rot = t.rotation;
            p.Since = -1f;
            p.Touched = false;
        }

        /// <summary>This object is where every game is told (or told by another) it is.</summary>
        private void Rebase(GameObject go)
        {
            Rigidbody b = null;
            if (go != null)
            {
                b = go.GetComponent<Rigidbody>();
            }
            if (b != null && _loose.TryGetValue(b, out var p) && p.Item != null)
            {
                Rebase(p);
            }
        }

        private void Send(Item item, ObjRef r, bool last)
        {
            if (item == null || !r.Valid)
            {
                return;
            }
            var t = item.transform;
            var w = Message(r, t.position, t.rotation, last);
            if (last)
            {
                Rebase(item.gameObject);
                Recell(item.gameObject); // it goes into the culling node of where it is now
            }
            int channel;
            if (last)
            {
                channel = Channel.Control;
            }
            else
            {
                channel = Channel.State;
            }
            if (_session.IsHost)
            {
                _session.Broadcast(w, channel, reliable: last);
            }
            else
            {
                _session.SendToHost(w, channel, reliable: last);
                if (last)
                {
                    if (!_unechoed.TryGetValue(item.gameObject, out var sent))
                    {
                        _unechoed[item.gameObject] = sent = new List<Vector3>();
                    }
                    if (sent.Count < 16)
                    {
                        sent.Add(t.position);
                    }
                }
            }
        }

        /// <summary>
        /// Client: this ObjMove is the host passing back where we said the thing came to rest (the host sends every
        /// rest position to everyone, its mover too). Our copy is there already, or our player has pushed it on since,
        /// and then putting it back there would undo that push for a moment (the next rest is sent anyway): with a
        /// link of 100 ms or more, an echo came in while our player was still pushing, and the thing jumped back and
        /// held still under them. Anything else said about it (the host's own word, a refusal, another player's move)
        /// isn't ours and is taken, and the echoes still to come after it are taken too, so every game ends with the
        /// host's last word, as before.
        /// </summary>
        private bool OurEcho(GameObject go, Vector3 pos)
        {
            if (!_unechoed.TryGetValue(go, out var sent))
            {
                return false;
            }
            int i = sent.FindIndex(p => (p - pos).sqrMagnitude < 0.01f);
            if (i < 0)
            {
                _unechoed.Remove(go);
                return false;
            }
            sent.RemoveRange(0, i + 1);
            if (sent.Count == 0)
            {
                _unechoed.Remove(go);
            }
            return true;
        }

        /// <summary>ObjMove: ObjRef, vec3 pos, quat rot, u8 last.</summary>
        private PacketWriter Message(ObjRef r, Vector3 pos, Quaternion rot, bool last)
        {
            var w = _session.Begin(MsgType.ObjMove);
            r.Write(w);
            w.Write(pos);
            w.W.Write(rot.x);
            w.W.Write(rot.y);
            w.W.Write(rot.z);
            w.W.Write(rot.w);
            int value = 0;
            if (last)
            {
                value = 1;
            }
            w.W.Write((byte)value);
            return w;
        }

        /// <summary>
        /// Host: a player drags only what they're at, to near where they are. If not, the drag isn't made or passed
        /// on, and their copy is put back where the thing is here (ObjMove, last).
        /// </summary>
        private bool DragAllowed(ulong from, ObjRef obj, Vector3 pos)
        {
            var item = ObjRef.Resolve<Item>(obj);
            if (item == null)
            {
                return false;
            }
            if (Authority.Near(from, item.transform.position, WorldActions.Reach) && Authority.Near(from, pos, WorldActions.Reach))
            {
                return true;
            }
            Plugin.Log.LogInfo($"[authority] {_session.NameOf(from)} dragged {item.name} from too far: put back");
            _session.SendTo(from, Message(obj, item.transform.position, item.transform.rotation, true), Channel.Control, reliable: true);
            return false;
        }

        private void OnMove(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld)
            {
                return;
            }
            if (s.IsClient && from != s.HostId)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            var pos = r.ReadVector3();
            var rot = new Quaternion(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            bool last = (r.R.ReadByte() & 1) != 0;

            if (s.IsHost && !TakeMove(from, obj, pos, last))
            {
                return;
            }
            // Pass a client's drag on. Where it ended up goes back to its mover too: if the host's game had sent a push
            // of its own for the same object at the same time, the mover got that one and would keep it.
            if (s.IsHost)
            {
                var w = Message(obj, pos, rot, last);
                int channel = Channel.State;
                ulong skip = from;
                if (last)
                {
                    channel = Channel.Control;
                    skip = 0UL;
                }
                s.Broadcast(w, channel, reliable: last, except: skip);
            }

            var item = ObjRef.Resolve<Item>(obj);
            if (item == null)
            {
                if (s.IsClient && last && obj.K == ObjRef.Kind.Spawned)
                {
                    HostIds.HostMoved(obj.Id, pos); // for when our copy is tied
                }
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[world] got ObjMove {obj}: not found here");
                }
                return;
            }
            var go = item.gameObject;
            if (s.IsClient && _owners.TryGetValue(obj.ToString(), out var o))
            {
                o.Heard = Time.unscaledTime; // its drag goes on
            }
            if (item == _dragging) // we're holding it: kept for if we have to let go
            {
                _hostAt[go] = (pos, rot);
                return;
            }
            if (s.IsClient && last && OurEcho(go, pos))
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[world] {go.name} ({obj}): the host passed back where we left it");
                }
                return;
            }
            if (!_moving.TryGetValue(go, out var m))
            {
                ObjRef.Pin(go); // before it moves, so it's still found by the same name
                Combat.NetPlayerProxy.IgnoreWith(go);
                var body = go.GetComponent<Rigidbody>();
                m = new Moving { T = go.transform, Body = body, WasKinematic = body != null && body.isKinematic };
                if (body != null)
                {
                    body.isKinematic = true;
                }
                _moving[go] = m;
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[world] {s.NameOf(from)} is moving {go.name} ({obj})");
                }
            }
            m.Pos = pos;
            m.Rot = rot;
            m.Last = last;
            m.Heard = Time.unscaledTime;
        }

        // ---- who drags what --------------------------------------------------------------------------------------

        private bool Stale(Owner o)
        {
            float now = Time.unscaledTime;
            float heard = o.Heard;
            float limit = StaleAfter + 2f; // a client waits for the host's word
            if (_session.IsHost)
            {
                limit = StaleAfter;
            }
            return now - heard > limit;
        }

        /// <summary>The name of someone else who is dragging this object now (as the host decided), or null.</summary>
        public string OtherOwner(ObjRef r)
        {
            if (!r.Valid || !_owners.TryGetValue(r.ToString(), out var o) || o.Id == _session.LocalId || Stale(o))
            {
                return null;
            }
            return _session.NameOf(o.Id);
        }

        /// <summary>Our player has just taken hold of item: ours if nobody else drags it (the host is asked, on a client).</summary>
        private void StartDrag(Item item)
        {
            _dragRef = ObjRef.Pin(item.gameObject);
            _hostAt.Remove(item.gameObject);
            string other = OtherOwner(_dragRef);
            if (other != null)
            {
                CancelLocal(item, $"{other} is moving it");
                return;
            }
            if (_moving.TryGetValue(item.gameObject, out var m)) // ours now
            {
                Release(m);
                _moving.Remove(item.gameObject);
            }
            _nextSend = 0f;
            if (!_dragRef.Valid)
            {
                return;
            }
            if (_session.IsHost)
            {
                _owners[_dragRef.ToString()] = new Owner { Id = _session.LocalId, Heard = Time.unscaledTime, Ref = _dragRef };
                SendOwner(_dragRef, _session.LocalId, letGo: true);
                return;
            }
            var w = _session.Begin(MsgType.DragClaim);
            _dragRef.Write(w);
            Authority.Request(w, $"drag {item.name} ({_dragRef})", (ok, reason, _) =>
            {
                if (_dragging != item)
                {
                    return; // let go meanwhile, or already cancelled
                }
                if (ok)
                {
                    _claimed = true;
                    _nextSend = 0f;
                }
                else
                {
                    string why;
                    if (reason.EndsWith("is moving it"))
                    {
                        why = reason;
                    }
                    else
                    {
                        why = Language.Get("Playermsg_tooFar", "UI");
                    }
                    CancelLocal(item, why);
                }
            }, inventory: false);
        }

        /// <summary>Our player let go of what they dragged: the host frees the drag (a client's is freed by its last move).</summary>
        private void LetGo(ObjRef r)
        {
            if (!r.Valid || !_owners.TryGetValue(r.ToString(), out var o) || o.Id != _session.LocalId)
            {
                return;
            }
            _owners.Remove(r.ToString());
            if (_session.IsHost)
            {
                SendOwner(r, 0, letGo: false);
            }
        }

        /// <summary>
        /// Our player lets go of item at once (someone else is dragging it): no end position is sent, and our copy
        /// goes where the host last said it is, held still a moment as another game's drag is, then let go there.
        /// </summary>
        private void CancelLocal(Item item, string message)
        {
            if (item == null)
            {
                return;
            }
            if (_dragging == item)
            {
                _dragging = null;
                _claimed = false;
            }
            var go = item.gameObject;
            var player = Player.Instance;
            if (item.beingDragged)
            {
                item.stopDragging(force: true); // as the game lets go when the night ends
            }
            if (player != null && player.itemBeingDragged == item) // taken hold of by a script (the tests)
            {
                player.itemBeingDragged = null;
                var joint = item.GetComponent<HingeJoint>();
                if (joint != null)
                {
                    Object.Destroy(joint);
                }
            }
            if (!_moving.ContainsKey(go)) // not already shown following the other player's drag
            {
                var body = go.GetComponent<Rigidbody>();
                var at = (pos: go.transform.position, rot: go.transform.rotation);
                if (_hostAt.TryGetValue(go, out var h))
                {
                    at = h;
                }
                else if (body != null && _loose.TryGetValue(body, out var p))
                {
                    at = (p.Pos, p.Rot);
                }
                var m = new Moving { T = go.transform, Body = body, WasKinematic = body != null && body.isKinematic, Pos = at.pos, Rot = at.rot, Last = true, Heard = Time.unscaledTime };
                if (body != null)
                {
                    body.isKinematic = true;
                }
                _moving[go] = m;
                Combat.NetPlayerProxy.IgnoreWith(go);
            }
            _hostAt.Remove(go);
            Cancelled++;
            if (!string.IsNullOrEmpty(message))
            {
                if (player != null)
                {
                    player.displayMessage(message);
                }
            }
            Plugin.Log.LogInfo($"[dragging] let go of {item.name} ({ObjRef.Of(go)}): {message}");
        }

        private void SendOwner(ObjRef r, ulong owner, bool letGo, ulong to = 0)
        {
            var w = _session.Begin(MsgType.DragOwner);
            r.Write(w);
            w.W.Write(owner);
            int value = 0;
            if (letGo)
            {
                value = 1;
            }
            w.W.Write((byte)value);
            if (to != 0)
            {
                _session.SendTo(to, w, Channel.Control, reliable: true);
            }
            else
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>Host: where this object is here, as a drag's end, to one player (0: everyone), for their copy to go there.</summary>
        private void SendHere(ObjRef r, ulong to)
        {
            var item = ObjRef.Resolve<Item>(r);
            if (item == null)
            {
                return;
            }
            var w = Message(r, item.transform.position, item.transform.rotation, true);
            if (to != 0)
            {
                _session.SendTo(to, w, Channel.Control, reliable: true);
            }
            else
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>Host, a DragClaim inside a Request: the player took hold of this object. Theirs unless someone else drags it.</summary>
        private void OnClaim(ulong from, PacketReader r)
        {
            if (!_session.IsHost || !Authority.Handling)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            var item = ObjRef.Resolve<Item>(obj);
            if (item == null)
            {
                Authority.Refuse("it isn't here");
                return;
            }
            if (!Authority.InReach(item.transform.position, WorldActions.Reach, item.name))
            {
                SendHere(obj, from);
                return;
            }
            string key = obj.ToString();
            if (_owners.TryGetValue(key, out var o) && o.Id != from && !Stale(o))
            {
                SendHere(obj, from); // before the Verdict: where their copy goes as they let go
                Authority.Refuse($"{_session.NameOf(o.Id)} is moving it");
                return;
            }
            _owners[key] = new Owner { Id = from, Heard = Time.unscaledTime, Ref = obj };
            SendOwner(obj, from, letGo: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[dragging] {_session.NameOf(from)} drags {item.name} ({obj})");
            }
        }

        /// <summary>
        /// Host: whether a player's ObjMove is taken. While someone drags the thing, only their moves are (anyone else
        /// is sent where it is, at most twice a second); with nobody dragging it, a move that isn't a drag's end (a
        /// drag not agreed to) is dropped, and an end (a push come to rest) is taken as before. The mover must be
        /// within reach (DragAllowed): an owner who isn't loses the drag, and lets go.
        /// </summary>
        private bool TakeMove(ulong from, ObjRef obj, Vector3 pos, bool last)
        {
            string key = obj.ToString();
            bool owned = _owners.TryGetValue(key, out var o);
            if (owned && o.Id != from && Stale(o))
            {
                Free(key, o, "stopped sending");
                owned = false;
            }
            if (owned && o.Id != from)
            {
                string ck = key + "/" + from;
                if (!_corrected.TryGetValue(ck, out float at) || Time.unscaledTime - at > 0.5f)
                {
                    _corrected[ck] = Time.unscaledTime;
                    SendHere(obj, from);
                    Plugin.Log.LogInfo($"[dragging] {_session.NameOf(from)} moved {obj}, which {_session.NameOf(o.Id)} drags: not taken, sent where it is");
                }
                return false;
            }
            if (!owned && !last)
            {
                // A push still sliding (nobody drags it): passed on to be shown, if the pusher is at it. Where it comes
                // to rest is checked and taken as its last move.
                var it = ObjRef.Resolve<Item>(obj);
                if (it != null && Authority.Near(from, it.transform.position, WorldActions.Reach) && Authority.Near(from, pos, WorldActions.Reach))
                {
                    return true;
                }
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo($"[dragging] {_session.NameOf(from)} moved {obj} from too far without a claim: dropped");
                }
                return false;
            }
            if (!DragAllowed(from, obj, pos))
            {
                if (owned)
                {
                    _owners.Remove(key);
                    SendOwner(obj, 0, letGo: true);
                }
                return false;
            }
            if (owned)
            {
                o.Heard = Time.unscaledTime;
                if (last)
                {
                    _owners.Remove(key);
                    SendOwner(obj, 0, letGo: false);
                }
            }
            return true;
        }

        /// <summary>Host: a drag whose owner has gone quiet or left is nobody's: everyone is sent where the thing is here.</summary>
        private void Free(string key, Owner o, string why)
        {
            _owners.Remove(key);
            SendHere(o.Ref, 0);
            SendOwner(o.Ref, 0, letGo: true);
            Plugin.Log.LogInfo($"[dragging] {_session.NameOf(o.Id)}'s drag of {o.Ref} freed: {why}");
        }

        private void FreeStale()
        {
            if (_owners.Count == 0)
            {
                if (_corrected.Count > 0)
                {
                    _corrected.Clear();
                }
                return;
            }
            List<string> stale = null;
            foreach (var kv in _owners)
            {
                if (Stale(kv.Value))
                {
                    if (stale == null)
                    {
                        stale = new List<string>();
                    }
                    stale.Add(kv.Key);
                }
            }
            if (stale == null)
            {
                return;
            }
            foreach (var k in stale)
            {
                if (_session.IsHost)
                {
                    Free(k, _owners[k], "stopped sending");
                }
                else
                {
                    _owners.Remove(k);
                }
            }
        }

        private void FreeOwnedBy(ulong who)
        {
            if (!_session.IsHost)
            {
                return;
            }
            List<string> theirs = null;
            foreach (var kv in _owners)
            {
                if (kv.Value.Id == who)
                {
                    if (theirs == null)
                    {
                        theirs = new List<string>();
                    }
                    theirs.Add(kv.Key);
                }
            }
            if (theirs != null)
            {
                foreach (var k in theirs)
                {
                    Free(k, _owners[k], "they left");
                }
            }
        }

        /// <summary>Client: who drags this object now, as the host decided; let go of it if the host says so.</summary>
        private void OnDragOwner(ulong from, PacketReader r)
        {
            if (!_session.IsClient || from != _session.HostId || !WorldShare.InWorld)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            ulong owner = r.R.ReadUInt64();
            bool letGo = (r.R.ReadByte() & 1) != 0;
            string key = obj.ToString();
            if (owner == 0)
            {
                _owners.Remove(key);
            }
            else
            {
                _owners[key] = new Owner { Id = owner, Heard = Time.unscaledTime, Ref = obj };
            }
            if (_dragging == null || _dragRef.ToString() != key)
            {
                return;
            }
            if (owner == _session.LocalId)
            {
                _claimed = true;
                return;
            }
            if (letGo)
            {
                Item item = _dragging;
                string why;
                if (owner != 0)
                {
                    why = $"{_session.NameOf(owner)} is moving it";
                }
                else
                {
                    why = Language.Get("Playermsg_tooFar", "UI");
                }
                CancelLocal(item, why);
            }
        }

        /// <summary>For the tests (WorldTest): another player (owner; 0: nobody) drags this object, as if the host had agreed.</summary>
        internal void TestOwner(GameObject go, ulong owner)
        {
            var r = ObjRef.Pin(go);
            if (owner == 0)
            {
                _owners.Remove(r.ToString());
            }
            else
            {
                _owners[r.ToString()] = new Owner { Id = owner, Heard = Time.unscaledTime, Ref = r };
            }
        }

        /// <summary>Slide each object others are moving towards where they said it is; let go of it when they're done.</summary>
        private void ShowOthers()
        {
            if (_moving.Count == 0)
            {
                return;
            }
            List<GameObject> done = null;
            float k = 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime);
            foreach (var kv in _moving)
            {
                var m = kv.Value;
                if (kv.Key == null || m.T == null)
                {
                    if (done == null)
                    {
                        done = new List<GameObject>();
                    }
                    done.Add(kv.Key);
                    continue;
                }
                bool stale = Time.unscaledTime - m.Heard > StaleAfter;
                if (m.Last || stale)
                {
                    // Glide the last bit to where it came to rest, as it slid there in the mover's game (a jump of
                    // more than GlideMax, a correction, is made at once); then it's there exactly, and let go.
                    float off = (m.T.position - m.Pos).magnitude;
                    bool there = off < 0.5f && Quaternion.Angle(m.T.rotation, m.Rot) < 1f;
                    if (!there && off < GlideMax && !stale && Time.unscaledTime - m.Heard < GlideFor)
                    {
                        m.T.SetPositionAndRotation(Vector3.Lerp(m.T.position, m.Pos, k), Quaternion.Slerp(m.T.rotation, m.Rot, k));
                        continue;
                    }
                    m.T.SetPositionAndRotation(m.Pos, m.Rot);
                    if (there || Time.unscaledTime - m.Heard > GlideFor || stale)
                    {
                        Release(m);
                        Rebase(kv.Key);
                        Recell(kv.Key);
                        if (done == null)
                        {
                            done = new List<GameObject>();
                        }
                        done.Add(kv.Key);
                    }
                    continue;
                }
                m.T.SetPositionAndRotation(Vector3.Lerp(m.T.position, m.Pos, k), Quaternion.Slerp(m.T.rotation, m.Rot, k));
            }
            if (done != null)
            {
                foreach (var go in done)
                {
                    _moving.Remove(go);
                }
            }
        }

        /// <summary>
        /// An object another game moved goes into the culling node of where it is now, keeping its kind of cullable,
        /// and is shown or hidden as that node is (the game does the same after its player's drag). Otherwise it's
        /// still shown and hidden with the node it was in: missing when someone walks to its new place, or showing
        /// up far from it. CullableObject.checkForNewNode doesn't help: it runs only for moveables, and only while
        /// they're switched on.
        /// </summary>
        private static void Recell(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            var co = go.GetComponent<CullableObject>();
            var grid = Singleton<WorldGrid>.Instance;
            WorldGrid.Node old = null;
            if (co != null)
            {
                old = co.node;
            }
            if (old == null || grid == null || grid.currentGrid == null)
            {
                return;
            }
            if (grid.inVicinityOfNode(go.transform.position, old))
            {
                return;
            }
            if (!grid.currentGrid.nodes.Contains(old))
            {
                return; // in a grid this game isn't playing (a location kept for a joiner)
            }
            try
            {
                WorldGrid.Cullable cullable = old.getCullable(go);
                WorldGrid.Cullable.Type type;
                if (cullable != null)
                {
                    type = cullable.type;
                }
                else
                {
                    type = WorldGrid.Cullable.Type.normal;
                }
                grid.registerToNode(go, type);
                var now = co.node;
                WorldGrid.Cullable cul = null;
                if (now != null)
                {
                    cul = now.getCullable(go);
                }
                if (cul == null)
                {
                    return;
                }
                if (now.entered && !go.activeSelf)
                {
                    cul.show();
                }
                else if (!now.entered && go.activeSelf)
                {
                    cul.hide();
                }
                if (WorldActions.Debug)
                {
                    string text = "switched off";
                    if (now.entered)
                    {
                        text = "shown";
                    }
                    Plugin.Log.LogInfo($"[world] {go.name} moved to another culling node here ({text})");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't move {go.name} to its new culling node: {e.Message}");
            }
        }

        private static void Release(Moving m)
        {
            if (m.Body == null)
            {
                return;
            }
            m.Body.isKinematic = m.WasKinematic;
            m.Body.position = m.T.position;
            m.Body.rotation = m.T.rotation;
            if (!m.Body.isKinematic)
            {
                m.Body.velocity = Vector3.zero;
                m.Body.angularVelocity = Vector3.zero;
                m.Body.Sleep(); // where it was put: not to drift off by itself in this game
            }
        }
    }

    /// <summary>
    /// The player can't take hold of something another player is dragging (as the host decided, DragOwner): the game's
    /// drag doesn't start, and the player is told who is moving it.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.startDragging))]
    internal static class DragStartPatch
    {
        /// <summary>Drags not started (for the tests).</summary>
        internal static int Blocked;

        private static bool Prefix(Item __instance)
        {
            var d = DragSync.Instance;
            if (d == null || !WorldActions.Sharing || __instance == null)
            {
                return true;
            }
            string who = d.OtherOwner(ObjRef.Of(__instance.gameObject));
            if (who == null)
            {
                return true;
            }
            Player instance = Player.Instance;
            if (instance != null)
            {
                instance.displayMessage($"{who} is moving it");
            }
            Blocked++;
            return false;
        }
    }
}
