using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// The host's check on where a client says its player is. A client moves its own player at once (it can't
    /// wait for the host to walk it), so its movement is a prediction the host accepts while it's possible:
    /// over each half second, the player may not have gone faster than MaxSpeed. If they did, the host keeps them
    /// where they last were (that's where the others see them) and tells them (PlayerCorrection), and their game
    /// puts them back there.
    ///
    /// Moving further than Teleport in one update is the game moving the player itself (into a place outside the
    /// map, a dream, back home after dying, the loading spot), and starts the check again from there.
    /// </summary>
    public sealed class MoveCheck
    {
        /// <summary>Units a second; running is about a quarter of this.</summary>
        public const float MaxSpeed = 2000f;
        public const float Teleport = 3000f;
        private const float Window = 0.5f;

        private sealed class Track { public Vector3 Good; public Vector3 WindowStart; public float WindowAt; public float LastCorrection = -10f; }
        private readonly Dictionary<ulong, Track> _tracks = new Dictionary<ulong, Track>();
        private readonly Session _session;

        public static int Corrections { get; private set; }

        public MoveCheck(Session session)
        {
            _session = session;
            session.OnPeerDisconnected += id => _tracks.Remove(id);
            session.OnSessionEnded += () => _tracks.Clear();
            session.Register(MsgType.PlayerCorrection, OnCorrection);
        }

        /// <summary>Host: a client's player says it's at pos. Returns where it may be (pos, or where it was).</summary>
        /// <summary>Stop tracking this player: the next position they send is taken as it is.</summary>
        public void Forget(ulong from) => _tracks.Remove(from);

        public Vector3 Check(ulong from, Vector3 pos)
        {
            float now = Time.realtimeSinceStartup;
            if (!_tracks.TryGetValue(from, out var t) || Flat(pos - t.Good) > Teleport)
            {
                _tracks[from] = new Track { Good = pos, WindowStart = pos, WindowAt = now };
                return pos;
            }
            float dt = now - t.WindowAt;
            float speed = Flat(pos - t.WindowStart) / Mathf.Max(dt, Window);
            if (speed > MaxSpeed)
            {
                if (now - t.LastCorrection > Window)
                {
                    t.LastCorrection = now;
                    Corrections++;
                    Plugin.Log.LogInfo($"[authority] {_session.NameOf(from)} moved at {speed:0} a second (most {MaxSpeed:0}): put back");
                    var w = _session.Begin(MsgType.PlayerCorrection);
                    w.Write(t.Good);
                    _session.SendTo(from, w, Channel.Control, reliable: true);
                }
                t.WindowStart = t.Good; t.WindowAt = now;
                return t.Good;
            }
            t.Good = pos;
            if (dt >= Window) { t.WindowStart = pos; t.WindowAt = now; }
            return pos;
        }

        private static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        /// <summary>Client: the host put our player back.</summary>
        private void OnCorrection(ulong from, PacketReader r)
        {
            if (!_session.IsClient || from != _session.HostId) return;
            var pos = r.ReadVector3();
            var p = Player.Instance;
            if (p == null) return;
            Corrections++;
            Plugin.Log.LogInfo($"[authority] the host put our player back at {pos}");
            var body = p.GetComponent<Rigidbody>();
            p.transform.position = new Vector3(pos.x, p.transform.position.y, pos.z);
            if (body != null) { body.position = p.transform.position; body.velocity = Vector3.zero; }
        }
    }
}
