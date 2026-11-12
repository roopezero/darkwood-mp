using System.Collections.Generic;
using DarkwoodMP.Net;
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
    /// wherever it was last sent or heard to be). Four times a second each is compared with that; one that's more
    /// than half a unit or two degrees away and at rest again is settled, so a push between two checks is caught
    /// too. The host sends where it is as a drag's end (ObjMove, last), whatever moved it (its player, an explosion,
    /// an enemy, physics settling). A client sends only what its own player was at while it was away from there
    /// (the host checks it as it checks a drag: the mover within reach of the object and of where it went), and
    /// puts anything else back, however far it went (settling, a nudge, its copy of an explosion): if the host's
    /// copy moved too, the host's word on it comes as an ObjMove. So the last word is the host's resting position.
    /// An object put where another game said is left asleep, so it doesn't drift from there by itself.
    ///
    /// The object keeps the ObjRef it had before it moved (ObjRef.Pin), so the ones named by where they stand
    /// are still found after they've been moved.
    /// </summary>
    public sealed class DragSync
    {
        private const float SendInterval = 0.1f;
        private const float StaleAfter = 3f; // a mover that stopped sending (left, lost the last packet): let go

        private readonly Session _session;

        // the local drag
        private Item _dragging;
        private ObjRef _dragRef;
        private float _nextSend;
        private Item _settling;       // the object just let go of, sent again once it's come to rest
        private ObjRef _settlingRef;
        private float _settleAt;

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
        private const float PushWatchInterval = 0.25f;
        private const float PushMinMove = 0.5f;    // world units (a crate is about 50 across)
        private const float PushMinTurn = 2f;      // degrees
        private const float PushTouch = 120f;      // a client's player this close to a thing that moved moved it
        private const float PushGiveUpAfter = 10f; // still moving after this: sent as it is
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
            session.OnSessionEnded += () => { foreach (var m in _moving.Values) Release(m); _moving.Clear(); ForgetLoose(); _dragging = null; _settling = null; };
        }

        private void ForgetLoose() { _loose.Clear(); _looseFound = false; }

        public void Update()
        {
            ShowOthers();
            if (!WorldActions.Sharing) { _dragging = null; if (_loose.Count > 0 || _looseFound) ForgetLoose(); return; }
            var player = Player.Instance;
            var now = player != null ? player.itemBeingDragged : null;

            if (now != _dragging)
            {
                if (_dragging != null) // let go: where it stopped, now and once it has settled
                {
                    Send(_dragging, _dragRef, last: true);
                    _settling = _dragging; _settlingRef = _dragRef; _settleAt = Time.unscaledTime + 1.5f;
                }
                _dragging = now;
                if (now != null)
                {
                    _dragRef = ObjRef.Pin(now.gameObject);
                    if (_moving.TryGetValue(now.gameObject, out var m)) { Release(m); _moving.Remove(now.gameObject); } // ours now
                    _nextSend = 0f;
                }
            }

            if (_dragging != null && _dragRef.Valid && Time.unscaledTime >= _nextSend)
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
            if (player == null) return;
            var me = player.transform.position;
            if (!_looseFound)
            {
                _looseFound = true;
                // The host notes the switched-off ones too (where the save put them): a part of the world switched on
                // later around a client's player settles there (a chair, a table shifting a few units), and that is
                // then sent to everyone, so a client whose copy is still switched off has them settled too. Noted
                // only once seen, they had settled already, nothing was sent, and the map4 test found a third
                // client's copies 2 and 8 units from the host's and the two clients' there.
                foreach (var b in Object.FindObjectsOfType<Rigidbody>(_session.IsHost)) Note(b, null);
            }
            Look(me);
            if (_session.IsHost)
                foreach (var proxy in Combat.NetPlayerProxy.All.Values)
                    if (proxy != null && proxy.alive && !proxy.Away) Look(proxy.transform.position);
            // A client away (spectating) has no player of its own to push anything: it takes the host's word on all.
            bool clientAway = _session.IsClient && Sync.PlayerCapture.LocalAway();
            float nowT = Time.unscaledTime;
            List<Rigidbody> gone = null;
            foreach (var kv in _loose)
            {
                var p = kv.Value;
                if (p.Body == null || p.Item == null) { (gone ??= new List<Rigidbody>()).Add(kv.Key); continue; }
                var t = p.Item.transform;
                if (p.Body.isKinematic || p.Item == _dragging || p.Item == _settling || _moving.ContainsKey(p.Body.gameObject))
                {
                    Rebase(p); // where the drag or the other game puts it, which is sent or heard as that
                    continue;
                }
                float moved = (t.position - p.Pos).magnitude;
                if (moved < PushMinMove && Quaternion.Angle(t.rotation, p.Rot) < PushMinTurn) { p.Since = -1f; p.Touched = false; continue; }
                if (p.Since < 0f) p.Since = nowT;
                if (_session.IsClient && !clientAway && !p.Touched && p.Col != null && p.Col.bounds.SqrDistance(me) < PushTouch * PushTouch) p.Touched = true;
                if (!AtRest(p.Body) && nowT - p.Since < PushGiveUpAfter) continue;
                if (_session.IsClient && !p.Touched)
                {
                    // Not our player's doing (settling, a nudge, our copy of an explosion): the host decides where it
                    // is, so our copy goes back to where it was last known to be.
                    t.SetPositionAndRotation(p.Pos, p.Rot);
                    p.Body.position = p.Pos; p.Body.rotation = p.Rot;
                    p.Body.velocity = Vector3.zero; p.Body.angularVelocity = Vector3.zero;
                    p.Body.Sleep();
                    p.Since = -1f;
                    if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {p.Item.name} ({p.Ref}) moved {moved:0.0} units by itself: put back");
                    continue;
                }
                Send(p.Item, p.Ref, last: true); // which notes it there
                PushesSent++;
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] pushed {p.Item.name} ({p.Ref}) {moved:0.0} units: sent where it is");
            }
            if (gone != null) foreach (var b in gone) _loose.Remove(b);
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
                if (LooseItem(b) == null) continue;
                ObjRef.Pin(b.gameObject); // named by where the snapshot has it, which is where the joiner finds it
                _atSnapshot[b] = (b.transform.position, b.transform.rotation);
            }
            SnapshotAt = Time.unscaledTime;
        }

        /// <summary>Host: the world offered to joiners isn't a snapshot (LiveWorld off: the last save), so nothing is known of it.</summary>
        public static void ForgetSnapshot() { _atSnapshot.Clear(); SnapshotAt = -1f; }

        /// <summary>
        /// Host: a joiner is in the world it loaded (it asked for its character). Each loose object that's no longer
        /// where the snapshot had it, and is at rest, is sent to that joiner (ObjMove, last). One still moving is
        /// noted at its snapshot position, so it is sent to everyone once it's at rest (WatchPushes).
        /// </summary>
        public void JoinerInWorld(ulong who)
        {
            if (!_session.IsHost || _atSnapshot.Count == 0) return;
            int sent = 0, later = 0;
            foreach (var kv in _atSnapshot)
            {
                var b = kv.Key;
                if (b == null) continue;
                var t = b.transform;
                if ((t.position - kv.Value.pos).magnitude < PushMinMove && Quaternion.Angle(t.rotation, kv.Value.rot) < PushMinTurn) continue;
                var item = LooseItem(b);
                if (item == null || b.isKinematic || item == _dragging || _moving.ContainsKey(b.gameObject)) continue; // a drag sends its own end
                if (!AtRest(b))
                {
                    if (_loose.TryGetValue(b, out var p)) { p.Pos = kv.Value.pos; p.Rot = kv.Value.rot; p.Since = -1f; later++; }
                    continue;
                }
                var r = ObjRef.Pin(b.gameObject);
                if (!r.Valid) continue;
                var w = _session.Begin(MsgType.ObjMove);
                Write(w, r, t.position, t.rotation, true);
                _session.SendTo(who, w, Channel.Control, reliable: true);
                sent++;
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {item.name} ({r}) moved {(t.position - kv.Value.pos).magnitude:0.0} units since {_session.NameOf(who)}'s snapshot: sent where it is");
            }
            if (sent > 0 || later > 0) Plugin.Log.LogInfo($"{_session.NameOf(who)} is in the world: {sent} loose objects that moved since their snapshot sent to them{(later > 0 ? $", {later} still moving (sent once at rest)" : "")}");
        }

        /// <summary>
        /// Host: notes every loose object under root at once (a separate location it starts running for a joiner).
        /// Objects are otherwise noted only within PushWatchRadius of a player, and one an enemy pushes far from
        /// everyone (a dog chasing about a location while its joiner is elsewhere in it) would move unseen and stay
        /// where the host's enemy left it in the host's game only.
        /// </summary>
        public void NoteAll(GameObject root)
        {
            if (root == null || !_session.IsHost) return;
            foreach (var b in root.GetComponentsInChildren<Rigidbody>(true)) Note(b, null);
        }

        /// <summary>Notes the loose objects around center not noted yet (spawned, or come into the world since).</summary>
        private void Look(Vector3 center)
        {
            int n = Physics.OverlapSphereNonAlloc(center, PushWatchRadius, _near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var b = _near[i].attachedRigidbody;
                if (b != null && !_loose.ContainsKey(b)) Note(b, _near[i]);
            }
        }

        /// <summary>An Item with a rigidbody that can move, not a character, door or window.</summary>
        private static Item LooseItem(Rigidbody b)
        {
            if (b == null || b.constraints == RigidbodyConstraints.FreezeAll) return null;
            var item = b.GetComponent<Item>();
            if (item == null) return null;
            if (b.GetComponent<CharBase>() != null || b.GetComponentInParent<Door>() != null || b.GetComponentInParent<Window>() != null) return null;
            return item;
        }

        /// <summary>A loose object (an Item with a rigidbody that can move), noted where it is now.</summary>
        private void Note(Rigidbody b, Collider col)
        {
            if (b == null || _loose.ContainsKey(b)) return;
            var item = LooseItem(b);
            if (item == null) return;
            var r = ObjRef.Pin(b.gameObject);
            if (!r.Valid) return;
            var p = new Loose { Item = item, Body = b, Ref = r, Col = col != null ? col : b.GetComponentInChildren<Collider>() };
            Rebase(p);
            _loose[b] = p;
        }

        /// <summary>For the tests' log: whether this body is watched for pushes here, and where it's known to be.</summary>
        internal string WatchInfo(Rigidbody b)
        {
            if (b == null) return "no body";
            if (!_loose.TryGetValue(b, out var p)) return "not watched";
            return System.FormattableString.Invariant($"watched, known at {p.Pos.x:0.0} {p.Pos.z:0.0}{(p.Since >= 0f ? ", away" : "")}{(p.Touched ? ", touched" : "")}");
        }

        private static void Rebase(Loose p)
        {
            var t = p.Item.transform;
            p.Pos = t.position; p.Rot = t.rotation; p.Since = -1f; p.Touched = false;
        }

        /// <summary>This object is where every game is told (or told by another) it is.</summary>
        private void Rebase(GameObject go)
        {
            var b = go != null ? go.GetComponent<Rigidbody>() : null;
            if (b != null && _loose.TryGetValue(b, out var p) && p.Item != null) Rebase(p);
        }

        private void Send(Item item, ObjRef r, bool last)
        {
            if (item == null || !r.Valid) return;
            var t = item.transform;
            var w = _session.Begin(MsgType.ObjMove);
            Write(w, r, t.position, t.rotation, last);
            if (last) { Rebase(item.gameObject); Recell(item.gameObject); } // and into the culling node of where it is now
            bool reliable = last;
            int ch = last ? Channel.Control : Channel.State;
            if (_session.IsHost) _session.Broadcast(w, ch, reliable);
            else _session.SendToHost(w, ch, reliable);
        }

        private static void Write(PacketWriter w, ObjRef r, Vector3 pos, Quaternion rot, bool last)
        {
            r.Write(w);
            w.Write(pos);
            w.W.Write(rot.x); w.W.Write(rot.y); w.W.Write(rot.z); w.W.Write(rot.w);
            w.W.Write((byte)(last ? 1 : 0));
        }

        /// <summary>
        /// Host: a player drags only what they're at, to near where they are. If not, the drag isn't made or passed
        /// on, and their copy is put back where the thing is here (ObjMove, last).
        /// </summary>
        private bool DragAllowed(ulong from, ObjRef obj, Vector3 pos)
        {
            var item = ObjRef.Resolve<Item>(obj);
            if (item == null) return false;
            if (Authority.Near(from, item.transform.position, WorldActions.Reach) && Authority.Near(from, pos, WorldActions.Reach)) return true;
            Plugin.Log.LogInfo($"[authority] {_session.NameOf(from)} dragged {item.name} from too far: put back");
            var w = _session.Begin(MsgType.ObjMove);
            Write(w, obj, item.transform.position, item.transform.rotation, true);
            _session.SendTo(from, w, Channel.Control, reliable: true);
            return false;
        }

        private void OnMove(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld) return;
            if (s.IsClient && from != s.HostId) return;
            var obj = ObjRef.Read(r);
            var pos = r.ReadVector3();
            var rot = new Quaternion(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            bool last = (r.R.ReadByte() & 1) != 0;

            if (s.IsHost && !DragAllowed(from, obj, pos)) return;
            if (s.IsHost) // pass a client's drag on
            {
                var w = s.Begin(MsgType.ObjMove);
                Write(w, obj, pos, rot, last);
                // Where it ended up goes back to its mover too: if the host's game had sent a push of its own for the
                // same object at the same time, the mover got that one and would keep it.
                s.Broadcast(w, last ? Channel.Control : Channel.State, reliable: last, except: last ? 0UL : from);
            }

            var item = ObjRef.Resolve<Item>(obj);
            if (item == null) { if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got ObjMove {obj}: not found here"); return; }
            if (item == _dragging) return; // we're dragging it ourselves
            var go = item.gameObject;
            if (!_moving.TryGetValue(go, out var m))
            {
                ObjRef.Pin(go); // before it moves, so it's still found by the same name
                Combat.NetPlayerProxy.IgnoreWith(go);
                var body = go.GetComponent<Rigidbody>();
                m = new Moving { T = go.transform, Body = body, WasKinematic = body != null && body.isKinematic };
                if (body != null) body.isKinematic = true;
                _moving[go] = m;
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {s.NameOf(from)} is moving {go.name} ({obj})");
            }
            m.Pos = pos; m.Rot = rot; m.Last = last; m.Heard = Time.unscaledTime;
        }

        /// <summary>Slide each object others are moving towards where they said it is; let go of it when they're done.</summary>
        private void ShowOthers()
        {
            if (_moving.Count == 0) return;
            List<GameObject> done = null;
            float k = 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime);
            foreach (var kv in _moving)
            {
                var m = kv.Value;
                if (kv.Key == null || m.T == null) { (done ??= new List<GameObject>()).Add(kv.Key); continue; }
                bool stale = Time.unscaledTime - m.Heard > StaleAfter;
                if (m.Last || stale)
                {
                    m.T.SetPositionAndRotation(m.Pos, m.Rot);
                    if (Time.unscaledTime - m.Heard > 0.25f || stale) { Release(m); Rebase(kv.Key); Recell(kv.Key); (done ??= new List<GameObject>()).Add(kv.Key); }
                    continue;
                }
                m.T.SetPositionAndRotation(Vector3.Lerp(m.T.position, m.Pos, k), Quaternion.Slerp(m.T.rotation, m.Rot, k));
            }
            if (done != null) foreach (var go in done) _moving.Remove(go);
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
            if (go == null) return;
            var co = go.GetComponent<CullableObject>();
            var grid = Singleton<WorldGrid>.Instance;
            var old = co != null ? co.node : null;
            if (old == null || grid == null || grid.currentGrid == null) return;
            if (grid.inVicinityOfNode(go.transform.position, old)) return;
            if (!grid.currentGrid.nodes.Contains(old)) return; // in a grid this game isn't playing (a location kept for a joiner)
            try
            {
                var type = old.getCullable(go)?.type ?? WorldGrid.Cullable.Type.normal;
                grid.registerToNode(go, type);
                var now = co.node;
                var cul = now?.getCullable(go);
                if (cul == null) return;
                if (now.entered && !go.activeSelf) cul.show();
                else if (!now.entered && go.activeSelf) cul.hide();
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {go.name} moved to another culling node here ({(now.entered ? "shown" : "switched off")})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't move {go.name} to its new culling node: {e.Message}"); }
        }

        private static void Release(Moving m)
        {
            if (m.Body == null) return;
            m.Body.isKinematic = m.WasKinematic;
            m.Body.position = m.T.position;
            m.Body.rotation = m.T.rotation;
            if (!m.Body.isKinematic)
            {
                m.Body.velocity = Vector3.zero; m.Body.angularVelocity = Vector3.zero;
                m.Body.Sleep(); // where it was put: not to drift off by itself in this game
            }
        }
    }
}
