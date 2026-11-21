using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Dragging;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// DragSync on the host: where loose things are in the snapshot a
    /// joiner loads, and what moved since, sent once they're in.
    internal sealed partial class DragSync
    {
        // Host: every loose object where the last snapshot for joiners has it
        // (WorldShare.SaveSnapshot).
        private readonly
            Dictionary<Rigidbody, (Vector3 pos, Quaternion rot)> _atSnapshot =
                new Dictionary<Rigidbody, (Vector3, Quaternion)>();
        /// <summary>
        /// When the last snapshot's positions were noted (Time.unscaledTime;
        /// -1: none). For the tests.
        /// </summary>
        public float SnapshotAt { get; private set; } = -1f;

        /// <summary>
        /// Host, in the frame the world is read for a joiner's snapshot: where
        /// every loose object is in it. Something still moving then (a dropped
        /// item sliding off, furniture settling) is saved mid-way, and the
        /// joiner's game loads it there and keeps it there; the host's rest
        /// position, if sent while the joiner was still loading, was dropped
        /// (ObjMove is only taken in the world). JoinerInWorld sends the
        /// difference.
        /// </summary>
        public void NoteSnapshot()
        {
            _atSnapshot.Clear();
            foreach (var b in Object.FindObjectsOfType<Rigidbody>(true))
            {
                if (LooseItem(b) == null)
                {
                    continue;
                }

                // named by where the snapshot has it, which is where the joiner
                // finds it
                ObjRef.Pin(b.gameObject);
                _atSnapshot[b] = (b.transform.position, b.transform.rotation);
            }

            SnapshotAt = Time.unscaledTime;
        }

        /// <summary>
        /// Host: the world offered to joiners isn't a snapshot (LiveWorld off:
        /// the last save), so nothing is known of it.
        /// </summary>
        public void ForgetSnapshot()
        {
            _atSnapshot.Clear();
            SnapshotAt = -1f;
        }

        /// <summary>
        /// Host: a joiner is in the world it loaded (it asked for its
        /// character). Each loose object that's no longer where the snapshot
        /// had it, and is at rest, is sent to that joiner (ObjMove, last). One
        /// still moving is noted at its snapshot position, so it is sent to
        /// everyone once it's at rest (WatchPushes).
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
                // a drag sends its own end
                var place = PushRules.Joiner(FactsOf(b, kv.Value));
                if (place == JoinerPlace.Later)
                {
                    var p = _loose[b];
                    p.Pos = kv.Value.pos;
                    p.Rot = kv.Value.rot;
                    p.Since = -1f;
                    later++;
                    continue;
                }

                if (place != JoinerPlace.Send)
                {
                    continue;
                }

                var t = b.transform;
                var item = LooseItem(b);
                var r = ObjRef.Pin(b.gameObject);
                if (!r.Valid)
                {
                    continue;
                }

                _session.SendTo(
                    who, Message(r, t.position, t.rotation, true),
                    Channel.Control, reliable: true);
                sent++;
                if (WorldActions.Debug)
                {
                    float moved = (t.position - kv.Value.pos).magnitude;
                    Plugin.Log.LogInfo(
                        $"[world] {item.name} "
                            + $"({r}) moved {moved:0.0} units since "
                            + $"{_session.NameOf(who)}'s snapshot: "
                            + "sent where it is");
                }
            }

            if (sent > 0 || later > 0)
            {
                string moving = "";
                if (later > 0)
                {
                    moving = $", {later} still moving (sent once at rest)";
                }

                Plugin.Log.LogInfo(
                    $"{_session.NameOf(who)} is in the world: {sent} loose "
                        + "objects that moved since their snapshot sent to "
                        + $"them{moving}");
            }
        }

        /// <summary>
        /// What the push rules know of a body where the joiners' snapshot
        /// had it (at): the only code that reads it for them.
        /// </summary>
        private SnapshotFacts FactsOf(
            Rigidbody b, (Vector3 pos, Quaternion rot) at)
        {
            if (b == null)
            {
                return new SnapshotFacts { Gone = true };
            }

            var t = b.transform;
            var facts = new SnapshotFacts
            {
                Moved = (t.position - at.pos).magnitude,
                Turned = Quaternion.Angle(t.rotation, at.rot),
            };

            if (!PushRules.Away(facts.Moved, facts.Turned))
            {
                // the rest isn't read: it's skipped
                return facts;
            }

            var item = LooseItem(b);
            return facts with
            {
                Loose = item != null,
                Kinematic = b.isKinematic,
                Dragged = item != null && item == _dragging,
                Shown = _moving.ContainsKey(b.gameObject),
                AtRest = AtRest(b),
                Watched = _loose.ContainsKey(b),
            };
        }
    }
}
