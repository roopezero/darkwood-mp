using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Doors
{
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
    internal sealed class DoorPush
    {
        private readonly CoopSession _coop;
        private readonly DoorLeaf _leaves;

        // leaves another game is turning, here: towards the newest angle heard
        private sealed class Glide
        {
            public float Target, Heard;
            public bool WasKinematic;
        }

        private readonly Dictionary<Door, Glide> _glides =
            new Dictionary<Door, Glide>();
        // client: angles we sent, oldest first, until the host passes each back
        private readonly Dictionary<Door, LeafEchoes> _unechoed =
            new Dictionary<Door, LeafEchoes>();

        private sealed class Watched
        {
            public Door D;
            public Rigidbody Rb;
            public LeafWatch State;
        }

        private readonly Dictionary<Door, Watched> _watched =
            new Dictionary<Door, Watched>();
        private readonly Collider[] _near = new Collider[256];
        private float _nextLook;

        /// <summary>
        /// Map test: count a push of this door as our player's, wherever the
        /// player stands.
        /// </summary>
        internal Door TestTouch { get; set; }

        /// <summary>
        /// For the tests' log: this game watches the door's leaf for pushes.
        /// </summary>
        internal bool Watches(Door d) =>
            d != null && _watched.ContainsKey(d);

        public DoorPush(CoopSession coop, DoorLeaf leaves)
        {
            _coop = coop;
            _leaves = leaves;
            coop.Handle(MsgType.DoorLeaf, OnLeaf);
            // a leaf held for a glide is given back to the physics
            coop.Hook(EndGlides);
        }

        public void Update()
        {
            if (!_coop.Guards.Sharing)
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
        private LeafDoorFacts FactsOf(Watched w, Vector3 at)
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
        private LeafFrame FrameOf(Watched w, Vector3 at)
        {
            var d = w.D;
            return new LeafFrame
            {
                Watch = w.State,
                Now = new Seconds(Time.unscaledTime),
                Angle = DoorLeaf.Signed(d),
                Spin = w.Rb.angularVelocity.y,
                Host = _coop.Net.IsHost,
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
        private void Step(Watched w, LeafFrame frame)
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
        /// Door.open, with where the opener stood: DoorLeaf remembers the
        /// push first, for a leaf that's switched off here.
        /// </summary>
        internal void Opened(Door d, Vector3 opener)
        {
            _leaves.Opened(d, opener);
            Watch(d);
        }

        private void Watch(Door d)
        {
            var open = new LeafOpenFacts
            {
                Sharing = _coop.Guards.Sharing,
                Replaying = _coop.Guards.Replaying,
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

        private void Look(Vector3 at)
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

        private void Send(Door d, float angle)
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

        private void Post(Door d, float angle)
        {
            var r = ObjRef.Of(d.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var pw = Write(r, angle);
            if (_coop.Net.IsHost)
            {
                _coop.Net.Broadcast(pw, Channel.Control, reliable: true);
                return;
            }

            _coop.Net.SendToHost(pw, Channel.Control, reliable: true);
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
        private bool OurEcho(Door d, float angle)
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
        private void GlideTo(Door d, float angle)
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

        private void Glides()
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

                    _leaves.SetLeaf(d, step.Angle);
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
        private GlideFacts GlideOf(Door d, Glide g)
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
        private void Heard(Door d, float angle)
        {
            if (_watched.TryGetValue(d, out var w))
            {
                w.State = LeafWatchRules.Heard(w.State, angle);
            }
        }

        private void EndGlides()
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

        private PacketWriter Write(ObjRef r, float angle)
        {
            var pw = _coop.Net.Begin(MsgType.DoorLeaf);
            DoorLeafMessage.Write(pw, ObjRef.NameOf(r), angle);
            return pw;
        }

        private void OnLeaf(ulong from, PacketReader r)
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
                        $"[authority] {_coop.Net.NameOf(from)} pushed "
                            + $"{d.name}'s leaf to {angle:0} from too far: "
                            + "put back");
                    _coop.Net.SendTo(
                        from,
                        Write(obj, DoorLeaf.Signed(d)),
                        Channel.Control,
                        reliable: true);
                    return;
                case LeafAnswer.Relay:
                    // the pusher too: the host's last word
                    _coop.Net.Broadcast(
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
        private LeafHeardFacts HeardOf(ulong from, Door d, float angle)
        {
            var facts = new LeafHeardFacts
            {
                InWorld = WorldShare.InWorld,
                Host = _coop.Net.IsHost,
                Client = _coop.Net.IsClient,
                FromHost = from == _coop.Net.HostId,
                Angle = angle,
                Door = DoorLeaf.FactsOf(d),
            };

            if (_coop.Net.IsHost && facts.Door.HasBody)
            {
                var near = _coop.Authority.NearOf(
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
}
