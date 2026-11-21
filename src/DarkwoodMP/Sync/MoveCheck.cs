using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// The host's check on where a client says its player is. A client moves
    /// its own player at once (it can't wait for the host to walk it), so its
    /// movement is a prediction the host accepts while it's possible:
    /// over each half second, the player may not have gone faster than
    /// MaxSpeed. If they did, the host keeps them where they last were (that's
    /// where the others see them) and tells them (PlayerCorrection), and their
    /// game puts them back there.
    ///
    /// Moving further than Teleport in one update is the game moving the player
    /// itself (into a place outside the map, a dream, back home after dying,
    /// the loading spot), and starts the check again from there. A shorter trip
    /// the game makes (a scripted transportPlayerToObject, such as the village
    /// cellar's dive under water) is allowed when the host has just agreed to
    /// the event that makes it (ExpectTrip): a jump to near its destination
    /// within a few seconds starts the check again there, instead of being put
    /// back as a speed hack.
    ///
    /// The core decides (MoveRules) and remembers (MoveTracker); this reads
    /// the game's clock, logs, and sends or applies the correction. One
    /// lives for each session, in CoopSession.Moves.
    /// </summary>
    internal sealed class MoveCheck
    {
        /// <summary>
        /// Units a second; running is about a quarter of this.
        /// </summary>
        public const float MaxSpeed = MoveRules.MaxSpeed;
        public const float Teleport = MoveRules.Teleport;

        /// <summary>
        /// How near the trip's destination the player must land.
        /// </summary>
        public const float TripLanding = MoveRules.TripLanding;

        private readonly MoveTracker _tracker = new MoveTracker();
        private readonly Session _session;

        /// <summary>
        /// Corrections this session: sent (host) or applied (client).
        /// </summary>
        public int Corrections { get; private set; }

        public MoveCheck(CoopSession coop)
        {
            _session = coop.Net;
            _session.OnPeerDisconnected += OnPeerLeft;
            coop.Hook(() => _session.OnPeerDisconnected -= OnPeerLeft);
            coop.Handle(MsgType.PlayerCorrection, OnCorrection);
        }

        private void OnPeerLeft(ulong id)
        {
            _tracker.Leave(id);
        }

        private static Seconds Now => new Seconds(Time.realtimeSinceStartup);

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>
        /// Where this player last was, as the check let them be (their newest
        /// PlayerState, or where they were put back).
        /// </summary>
        public bool TryAccepted(ulong from, out Vector3 pos)
        {
            bool known = _tracker.TryAccepted(from, out var accepted);
            pos = VectorOf(accepted);
            return known;
        }

        /// <summary>
        /// Stop tracking this player: the next position they send is taken as
        /// it is.
        /// </summary>
        public void Forget(ulong from) => _tracker.Forget(from);

        /// <summary>
        /// Host: an event it agreed to will take this player to
        /// <paramref name="to"/> (the game's own trip, within
        /// <paramref name="seconds"/>). Their first position near there in that
        /// time is taken as a teleport.
        /// </summary>
        public void ExpectTrip(ulong from, Vector3 to, float seconds)
        {
            var until = new Seconds(Time.realtimeSinceStartup + seconds);
            _tracker.ExpectTrip(from, VecOf(to).Ground, until);
        }

        /// <summary>
        /// Host: a client's player says it's at pos. Returns where it may be
        /// (pos, or where it was).
        /// </summary>
        public Vector3 Check(ulong from, Vector3 pos)
        {
            var step = _tracker.Check(from, VecOf(pos), Now);
            if (step.Outcome == MoveOutcome.Tripped)
            {
                Plugin.Log.LogInfo(
                    $"[authority] {_session.NameOf(from)} was taken to "
                        + $"{pos} by an event the host agreed to");
            }

            if (step.SendCorrection)
            {
                Corrections++;
                Plugin.Log.LogInfo(
                    $"[authority] {_session.NameOf(from)} moved at "
                        + $"{step.Speed:0} a second (most {MaxSpeed:0}): put "
                        + "back");
                var w = _session.Begin(MsgType.PlayerCorrection);
                PlayerCorrection.Write(w, step.Accepted);
                _session.SendTo(from, w, Channel.Control, reliable: true);
            }

            if (step.Outcome == MoveOutcome.PutBack)
            {
                return VectorOf(step.Accepted);
            }

            return pos;
        }

        /// <summary>Client: the host put our player back.</summary>
        private void OnCorrection(ulong from, PacketReader r)
        {
            if (!_session.IsClient || from != _session.HostId)
            {
                return;
            }

            var pos = VectorOf(PlayerCorrection.Read(r));
            var p = Player.Instance;
            if (p == null)
            {
                return;
            }

            Corrections++;
            Plugin.Log.LogInfo(
                $"[authority] the host put our player back at {pos}");
            var body = p.GetComponent<Rigidbody>();
            p.transform.position =
                new Vector3(pos.x, p.transform.position.y, pos.z);
            if (body != null)
            {
                body.position = p.transform.position;
                body.velocity = Vector3.zero;
            }
        }
    }
}
