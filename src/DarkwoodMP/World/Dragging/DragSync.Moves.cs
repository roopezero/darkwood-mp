using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Dragging;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// DragSync's ObjMoves: sending them, taking them in and showing other
    /// games' moves here.
    internal sealed partial class DragSync
    {
        private void Remember(GameObject go, Vector3 pos)
        {
            if (!_unechoed.TryGetValue(go, out var sent))
            {
                _unechoed[go] = sent = new EchoList();
            }

            sent.Sent(VecOf(pos));
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
                // it goes into the culling node of where it is now
                Recell(item.gameObject);
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
                    Remember(item.gameObject, t.position);
                }
            }
        }

        /// <summary>
        /// Client: this ObjMove is the host passing back where we said the
        /// thing came to rest (the host sends every rest position to everyone,
        /// its mover too). Our copy is there already, or our player has pushed
        /// it on since, and then putting it back there would undo that push for
        /// a moment (the next rest is sent anyway): with a link of 100 ms or
        /// more, an echo came in while our player was still pushing, and the
        /// thing jumped back and held still under them. Anything else said
        /// about it (the host's own word, a refusal, another player's move)
        /// isn't ours and is taken, and the echoes still to come after it are
        /// taken too, so every game ends with the host's last word, as before.
        /// </summary>
        private bool OurEcho(GameObject go, Vector3 pos)
        {
            if (!_unechoed.TryGetValue(go, out var sent))
            {
                return false;
            }

            bool ours = sent.Take(VecOf(pos));
            if (sent.Empty)
            {
                _unechoed.Remove(go);
            }

            return ours;
        }

        /// <summary>ObjMove: ObjRef, vec3 pos, quat rot, u8 last.</summary>
        private PacketWriter Message(
            ObjRef r, Vector3 pos, Quaternion rot, bool last)
        {
            var w = _session.Begin(MsgType.ObjMove);
            var move = new Moved
            {
                Name = ObjRef.NameOf(r),
                Pos = VecOf(pos),
                Rot = RotationOf(rot),
                Last = last,
            };

            ObjMove.Write(w, move);
            return w;
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private static Rotation RotationOf(Quaternion q) =>
            new Rotation(q.x, q.y, q.z, q.w);

        private static Quaternion QuaternionOf(Rotation q) =>
            new Quaternion(q.X, q.Y, q.Z, q.W);

        private void OnMove(ulong from, PacketReader r)
        {
            var s = _session;
            var arrival = new Dragging.ArrivalFacts
            {
                InWorld = WorldShare.InWorld,
                Host = s.IsHost,
                Client = s.IsClient,
                FromHost = from == s.HostId,
            };

            var arrive = ObjMoveRules.Arrive(arrival);
            if (arrive == Arrival.Drop)
            {
                return;
            }

            var move = ObjMove.Read(r);
            var obj = ObjRef.RefOf(move.Name);
            var pos = VectorOf(move.Pos);
            var rot = QuaternionOf(move.Rot);
            bool last = move.Last;

            if (arrive == Arrival.Check && !TakeMove(from, move))
            {
                return;
            }

            // Pass a client's drag on. Where it ended up goes back to its mover
            // too: if the host's game had sent a push of its own for the same
            // object at the same time, the mover got that one and would keep
            // it.
            if (s.IsHost)
            {
                var w = Message(obj, pos, rot, last);
                int channel = Channel.State;
                if (last)
                {
                    channel = Channel.Control;
                }

                ulong skip = ObjMoveRules.RelayExcept(last, from);
                s.Broadcast(w, channel, reliable: last, except: skip);
            }

            var item = ObjRef.Resolve<Item>(obj);
            var apply = new ApplyFacts
            {
                Client = s.IsClient,
                Found = item != null,
                Last = last,
                Spawned = obj.K == ObjRef.Kind.Spawned,
                Owned = _owners.ContainsKey(obj.ToString()),
                Ours = item != null && item == _dragging,
            };

            var step = ObjMoveRules.Apply(apply);
            if (step.Action == ApplyAction.NotHere
                || step.Action == ApplyAction.TellHostIds)
            {
                if (step.Action == ApplyAction.TellHostIds)
                {
                    // for when our copy is tied
                    CoopSession coop = Coop.Current;
                    if (coop != null)
                    {
                        coop.HostIds.HostMoved(obj.Id, pos);
                    }
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] got ObjMove {obj}: not found here");
                }

                return;
            }

            var go = item.gameObject;
            if (step.Refresh)
            {
                // its drag goes on
                _owners[obj.ToString()].Heard = Time.unscaledTime;
            }

            // we're holding it: kept for if we have to let go
            if (step.Action == ApplyAction.KeepHostWord)
            {
                _hostAt[go] = (pos, rot);
                return;
            }

            if (step.Action == ApplyAction.CheckEcho && OurEcho(go, pos))
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {go.name} ({obj}): the host passed back "
                            + "where we left it");
                }

                return;
            }

            if (!_moving.TryGetValue(go, out var m))
            {
                // before it moves, so it's still found by the same name
                ObjRef.Pin(go);
                Combat.NetPlayerProxy.IgnoreWith(go);
                var body = go.GetComponent<Rigidbody>();
                m = new Moving
                {
                    T = go.transform,
                    Body = body,
                    WasKinematic = body != null && body.isKinematic
                };

                if (body != null)
                {
                    body.isKinematic = true;
                }

                _moving[go] = m;
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {s.NameOf(from)} is moving {go.name} "
                            + $"({obj})");
                }
            }

            m.Pos = pos;
            m.Rot = rot;
            m.Last = last;
            m.Heard = Time.unscaledTime;
        }

        /// <summary>
        /// Slide each object others are moving towards where they said it is;
        /// let go of it when they're done.
        /// </summary>
        private void ShowOthers()
        {
            if (_moving.Count == 0)
            {
                return;
            }

            List<GameObject> done = null;
            float k = GlideRules.Blend(new Seconds(Time.unscaledDeltaTime));
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

                // Slide towards where it is; glide the last bit to where it
                // came to rest, as it slid there in the mover's game (a jump
                // of more than GlideRules.GlideMax, a correction, is made at
                // once); then it's there exactly, and let go.
                var glide = GlideRules.Step(FactsOf(m));
                if (glide == GlideAction.Glide)
                {
                    m.T.SetPositionAndRotation(
                        Vector3.Lerp(m.T.position, m.Pos, k),
                        Quaternion.Slerp(m.T.rotation, m.Rot, k));
                    continue;
                }

                m.T.SetPositionAndRotation(m.Pos, m.Rot);
                if (glide == GlideAction.Release)
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
        /// An object another game moved goes into the culling node of where it
        /// is now, keeping its kind of cullable, and is shown or hidden as that
        /// node is (the game does the same after its player's drag). Otherwise
        /// it's still shown and hidden with the node it was in: missing when
        /// someone walks to its new place, or showing up far from it.
        /// CullableObject.checkForNewNode doesn't help: it runs only for
        /// moveables, and only while they're switched on.
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
                // in a grid this game isn't playing (a location kept for a
                // joiner)
                return;
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

                    Plugin.Log.LogInfo(
                        $"[world] {go.name} moved to another culling node here "
                            + $"({text})");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't move {go.name} to its new culling node: "
                        + $"{e.Message}");
            }
        }

        /// <summary>
        /// What the glide rules know of a thing shown following another
        /// game's move: the only code that reads it for them.
        /// </summary>
        private static GlideFacts FactsOf(Moving m)
        {
            return new GlideFacts
            {
                Off = (m.T.position - m.Pos).magnitude,
                Turn = Quaternion.Angle(m.T.rotation, m.Rot),
                Last = m.Last,
                Heard = new Seconds(m.Heard),
                Now = Now,
            };
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
                // where it was put: not to drift off by itself in this game
                m.Body.Sleep();
            }
        }
    }
}
