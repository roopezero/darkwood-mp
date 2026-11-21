using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Dragging;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Furniture and other objects a player drags (Item.startDragging: a hinge
    /// joint to the player's body), shared.
    ///
    /// While the local player drags something, its position goes out ten times
    /// a second (unreliably), and where it ends up goes out reliably when the
    /// drag ends, and again once it has settled. The other games hold the
    /// object still (kinematic) and slide it to each position they get, then
    /// let its physics go again when the drag is over. A client's drags go to
    /// the host, which moves the object too and passes them on.
    ///
    /// Other players never shove loose things in a game that isn't theirs: on
    /// the host their stand-ins for the AI (NetPlayerProxy) don't collide with
    /// them, and the puppets in every game have no solid colliders. So a loose
    /// thing only moves in a game because that game's player moved it, or
    /// because the world did.
    ///
    /// Pushed objects too: every loose object (an Item with a rigidbody:
    /// furniture, placed objects, dropped items) is noted where it is known to
    /// be in every game (where it was when sharing began or it was first seen,
    /// then wherever it was last sent or heard to be). Ten times a second each
    /// is compared with that; one that's more than half a unit or two degrees
    /// away and at rest again is settled, so a push between two checks is
    /// caught too. While it's still sliding, where it is goes out too,
    /// unreliably, as a drag's moves do, so the other games show it sliding
    /// rather than jumping to its rest; they glide the last bit to where it
    /// came to rest. The host sends where it is as a drag's end (ObjMove,
    /// last), whatever moved it (its player, an explosion, an enemy, physics
    /// settling). A client sends only what its own player was at while it was
    /// away from there, or hit in the seconds before (a shot pushes from afar;
    /// the host checks it as it checks a drag: the mover within reach of the
    /// object and of where it went), and puts anything else back, however far
    /// it went (settling, a nudge, its copy of an explosion): if the host's
    /// copy moved too, the host's word on it comes as an ObjMove. So the last
    /// word is the host's resting position. The host passes each rest back to
    /// its mover too; a client skips that echo of its own word (OurEcho), which
    /// would put the thing back under its player if they had pushed it on in
    /// the meantime.
    /// An object put where another game said is left asleep, so it doesn't
    /// drift from there by itself.
    ///
    /// The object keeps the ObjRef it had before it moved (ObjRef.Pin), so the
    /// ones named by where they stand are still found after they've been moved.
    ///
    /// One player drags a thing at a time, and the host decides who (DragClaim,
    /// DragOwner). A client that takes hold of something asks the host for it
    /// (a Request) and sends its positions only once the host agrees; the host
    /// refuses it while someone else is dragging it, and the client then lets
    /// go at once and is shown where the thing is ("NAME is moving it"). The
    /// host's own player takes hold of it the same way, against the same list.
    /// The host takes a drag's moves only from its owner (anyone else is sent
    /// where the thing is), and frees the drag when its owner lets go, stops
    /// sending for DragRules.StaleAfter seconds, leaves, or drags it out of
    /// reach. Every game is told who owns each drag, so a player who tries to
    /// take hold of something someone else is moving is stopped before the
    /// game starts the drag (Item.startDragging).
    /// </summary>
    internal sealed partial class DragSync
    {
        // the times and distances are the rules' (OurDragRules, DragRules,
        // GlideRules, PushRules)
        private readonly Session _session;

        // the local drag
        private Item _dragging;
        private ObjRef _dragRef;
        private float _nextSend;
        // the object just let go of, sent again once it's come to rest
        private Item _settling;
        private ObjRef _settlingRef;
        private float _settleAt;
        // client: the host agreed that our player drags _dragging (we send its
        // moves)
        private bool _claimed;
        // client: where the host (or the player who owns the drag) last said
        // the thing we're holding is, for letting go
        private readonly Dictionary<GameObject, (Vector3 pos, Quaternion rot)>
            _hostAt = new Dictionary<GameObject, (Vector3, Quaternion)>();
        // client: where we said a thing came to rest, oldest first, until the
        // host passes each back to us (its echo)
        private readonly Dictionary<GameObject, EchoList> _unechoed =
            new Dictionary<GameObject, EchoList>();

        // who is dragging what (by ObjRef): decided on the host, as the host
        // said on a client
        private sealed class Owner
        {
            public ulong Id;
            public float Heard;
            public ObjRef Ref;
        }

        private readonly Dictionary<string, Owner> _owners =
            new Dictionary<string, Owner>();
        // host: a non-owner last sent where it is
        private readonly Dictionary<string, float> _corrected =
            new Dictionary<string, float>();
        private float _nextFree;
        /// <summary>
        /// Drags of ours cancelled because someone else had it, this session
        /// (for the tests' log).
        /// </summary>
        public int Cancelled { get; private set; }

        /// <summary>
        /// Drags the game didn't start because someone else had the thing
        /// (DragPatches), this session (for the tests).
        /// </summary>
        public int Blocked { get; private set; }

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

        private readonly Dictionary<GameObject, Moving> _moving =
            new Dictionary<GameObject, Moving>();

        // new loose things noted around the players
        private float _nextLook;
        // loose objects and where they're known to be, checked until they're
        // back at rest wherever they are
        private sealed class Loose
        {
            public Item Item;
            public Rigidbody Body;
            public Collider Col;
            public ObjRef Ref;
            // where it's known to be in every game: as loaded, or as last sent
            // or heard
            public Vector3 Pos;
            public Quaternion Rot;
            // when it was first seen away from there (-1: it isn't)
            public float Since = -1f;
            // a client's own player was at it while it was away from there
            public bool Touched;
            // when our player last hit it (a shot pushes from afar)
            public float HitAt = -1f;
        }

        private readonly Dictionary<Rigidbody, Loose> _loose =
            new Dictionary<Rigidbody, Loose>();
        // every loose object in the world so far has been noted
        private bool _looseFound;
        private readonly Collider[] _near = new Collider[PushRules.LookMost];
        private float _nextPushWatch;
        /// <summary>
        /// Pushed objects this game sent this session (for the tests' log).
        /// </summary>
        public int PushesSent { get; private set; }

        public DragSync(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.ObjMove, OnMove);
            coop.Handle(MsgType.DragClaim, OnClaim);
            coop.Handle(MsgType.DragOwner, OnDragOwner);
            _session.OnPeerDisconnected += FreeOwnedBy;
            coop.Hook(() => _session.OnPeerDisconnected -= FreeOwnedBy);
            // what others were moving is let go of where it is
            coop.Hook(ReleaseShown);
        }

        /// <summary>
        /// Item.startDragging, before the game starts our player's drag: not
        /// of something another player is dragging (as the host decided,
        /// DragOwner). Then the player is told who is moving it.
        /// </summary>
        public bool MayStart(Item item)
        {
            if (!Coop.Guards.Sharing || item == null)
            {
                return true;
            }

            string who = OtherOwner(ObjRef.Of(item.gameObject));
            if (who == null)
            {
                return true;
            }

            Player instance = Player.Instance;
            if (instance != null)
            {
                instance.displayMessage(MovingText(who));
            }

            Blocked++;
            return false;
        }

        private void ReleaseShown()
        {
            foreach (var m in _moving.Values)
            {
                Release(m);
            }

            _moving.Clear();
        }

        private void ForgetLoose()
        {
            _loose.Clear();
            _looseFound = false;
        }

        public void Update()
        {
            ShowOthers();
            if (!Coop.Guards.Sharing)
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
                // let go: where it stopped, now and once it has settled
                if (_dragging != null)
                {
                    Send(_dragging, _dragRef, last: true);
                    _settling = _dragging;
                    _settlingRef = _dragRef;
                    _settleAt = OurDragRules.SettleAt(Now).Value;
                    LetGo(_dragRef);
                }

                _dragging = now;
                _claimed = false;
                if (now != null)
                {
                    StartDrag(now);
                }
            }

            if (_dragging != null
                && OurDragRules.KeepsOursHeard(FactsOf(_dragRef)))
            {
                // the host's own drag never goes stale
                _owners[_dragRef.ToString()].Heard = Time.unscaledTime;
            }

            if (OurDragRules.Due(Now, new Seconds(_nextFree)))
            {
                _nextFree = Time.unscaledTime + DragRules.SweepEvery;
                FreeStale();
            }

            var send = new SendFacts
            {
                Dragging = _dragging != null,
                Named = _dragRef.Valid,
                Host = _session.IsHost,
                Claimed = _claimed,
                Now = Now,
                Next = new Seconds(_nextSend),
            };

            if (OurDragRules.SendDue(send))
            {
                _nextSend = OurDragRules.NextSend(Now).Value;
                Send(_dragging, _dragRef, last: false);
            }

            if (_settling != null
                && OurDragRules.Due(Now, new Seconds(_settleAt)))
            {
                Send(_settling, _settlingRef, last: true);
                _settling = null;
            }

            if (OurDragRules.Due(Now, new Seconds(_nextPushWatch)))
            {
                _nextPushWatch = Time.unscaledTime + PushRules.WatchEvery;
                WatchPushes(player);
            }
        }

        private static Seconds Now => new Seconds(Time.unscaledTime);

        private static bool AtRest(Rigidbody b)
        {
            return PushRules.AtRest(
                b.IsSleeping(), b.velocity.sqrMagnitude,
                b.angularVelocity.sqrMagnitude);
        }

        /// <summary>
        /// What the owner table says of this thing: the only code that reads
        /// _owners for the rules.
        /// </summary>
        private OwnerFacts FactsOf(ObjRef r)
        {
            var facts = new OwnerFacts
            {
                Named = r.Valid,
                Local = _session.LocalId,
                Now = Now,
                Host = _session.IsHost,
            };

            if (_owners.TryGetValue(r.ToString(), out var o))
            {
                facts = facts with
                {
                    Owned = true,
                    Owner = o.Id,
                    Heard = new Seconds(o.Heard),
                };
            }

            return facts;
        }

        /// <summary>
        /// What the owner table says of one of its entries.
        /// </summary>
        private OwnerFacts FactsOf(Owner o)
        {
            return new OwnerFacts
            {
                Named = o.Ref.Valid,
                Owned = true,
                Owner = o.Id,
                Local = _session.LocalId,
                Heard = new Seconds(o.Heard),
                Now = Now,
                Host = _session.IsHost,
            };
        }

        /// <summary>
        /// Our player has just taken hold of item: ours if nobody else drags it
        /// (the host is asked, on a client).
        /// </summary>
        private void StartDrag(Item item)
        {
            _dragRef = ObjRef.Pin(item.gameObject);
            _hostAt.Remove(item.gameObject);
            var start = new StartFacts
            {
                Other = DragRules.OtherOwner(FactsOf(_dragRef)),
                Named = _dragRef.Valid,
                Host = _session.IsHost,
            };

            var action = OurDragRules.Start(start);
            if (action == StartAction.Refused)
            {
                CancelLocal(item, MovingText(_session.NameOf(start.Other)));
                return;
            }

            // ours now
            if (_moving.TryGetValue(item.gameObject, out var m))
            {
                Release(m);
                _moving.Remove(item.gameObject);
            }

            _nextSend = 0f;
            if (action == StartAction.Unnamed)
            {
                return;
            }

            if (action == StartAction.Own)
            {
                _owners[_dragRef.ToString()] = new Owner
                {
                    Id = _session.LocalId,
                    Heard = Time.unscaledTime,
                    Ref = _dragRef
                };

                SendOwner(_dragRef, _session.LocalId, letGo: true);
                return;
            }

            var w = _session.Begin(MsgType.DragClaim);
            DragClaim.Write(w, ObjRef.NameOf(_dragRef));
            Coop.Current.Authority.Request(w, $"drag {item.name} ({_dragRef})",
                (ok, reason, _) =>
            {
                var reply = new ReplyFacts
                {
                    Same = _dragging == item,
                    Ok = ok,
                    Reason = reason,
                };

                // not the same: let go meanwhile, or already cancelled
                var answer = OurDragRules.Reply(reply);
                if (answer == ClaimReply.Claimed)
                {
                    _claimed = true;
                    _nextSend = 0f;
                }
                else if (answer == ClaimReply.ShowReason)
                {
                    CancelLocal(item, reason);
                }
                else if (answer == ClaimReply.TooFar)
                {
                    CancelLocal(item, TooFarText);
                }
            }, inventory: false);
        }

        /// <summary>
        /// Our player let go of what they dragged: the host frees the drag (a
        /// client's is freed by its last move).
        /// </summary>
        private void LetGo(ObjRef r)
        {
            var entry = OurDragRules.LetsGo(FactsOf(r));
            if (entry == LetGoEntry.Keep)
            {
                return;
            }

            _owners.Remove(r.ToString());
            if (entry == LetGoEntry.Tell)
            {
                SendOwner(r, 0, letGo: false);
            }
        }

        /// <summary>
        /// Our player lets go of item at once (someone else is dragging it): no
        /// end position is sent, and our copy goes where the host last said it
        /// is, held still a moment as another game's drag is, then let go
        /// there.
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
                // as the game lets go when the night ends
                item.stopDragging(force: true);
            }

            // taken hold of by a script (the tests)
            if (player != null && player.itemBeingDragged == item)
            {
                player.itemBeingDragged = null;
                var joint = item.GetComponent<HingeJoint>();
                if (joint != null)
                {
                    Object.Destroy(joint);
                }
            }

            // not already shown following the other player's drag
            var body = go.GetComponent<Rigidbody>();
            Loose p = null;
            if (body != null)
            {
                _loose.TryGetValue(body, out p);
            }

            var hold = new HoldFacts
            {
                Shown = _moving.ContainsKey(go),
                HostWord = _hostAt.ContainsKey(go),
                Known = body != null && p != null,
            };

            var holdAt = OurDragRules.Hold(hold);
            if (holdAt != HoldAt.None)
            {
                var at =
                    (pos: go.transform.position, rot: go.transform.rotation);
                if (holdAt == HoldAt.HostWord)
                {
                    at = _hostAt[go];
                }
                else if (holdAt == HoldAt.Known)
                {
                    at = (p.Pos, p.Rot);
                }

                var m = new Moving
                {
                    T = go.transform,
                    Body = body,
                    WasKinematic = body != null && body.isKinematic,
                    Pos = at.pos,
                    Rot = at.rot,
                    Last = true,
                    Heard = Time.unscaledTime
                };

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

            Plugin.Log.LogInfo(
                $"[dragging] let go of {item.name} ({ObjRef.Of(go)}): "
                    + $"{message}");
        }
    }
}
