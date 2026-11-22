using System;
using DarkwoodMP.Combat.Motion;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: how the host's enemies are moved between the host's states.
    ///
    /// Each EnemyState carries the host's clock (Time.unscaledTime) when it was
    /// sent. A client keeps each enemy's last few states by that time
    /// (MotionTrack), and every frame draws the enemy where the host has it
    /// now, as far as the states tell: between the two states around that
    /// moment when the next one is already here, or else carried on from the
    /// last one at the speed between the last two, for up to
    /// MotionTrack.MaxAhead. The host sends about every 50-70 ms
    /// (StateInterval, rounded up to its frames), and states come late or in
    /// bunches (TCP, the host's or our own slow frames, a lost one on Steam's
    /// unreliable channel), so drawing from the latest arrival would make
    /// enemies stop and jump. With the host's times the speed is the host's
    /// real speed, whenever the states came.
    ///
    /// The clock (MotionClock): the gap between our clock and the host's at
    /// each arrival is the host's clock offset plus how long the state took
    /// to come. The smallest gaps are the quickest arrivals; the offset
    /// follows them (it drops at once to a quicker one and rises slowly). The
    /// delay, how far behind that the enemies are drawn, is at most MaxDelay
    /// (30 ms; half of how much arrivals vary).
    ///
    /// Why not further behind, which is smoother (every frame between two real
    /// states): a copy of the host's enemy is solid here, and our player can't
    /// walk through it. Drawn 0.1-0.3 s behind the host, a dog coming at our
    /// player stands well short of where it is on the host, so our player walks
    /// into bites they seem to have dodged. Drawn at the host's present
    /// instead, a turning enemy is a little off until the next state comes.
    ///
    /// One per session (CoopSession.Motion): a new session starts with no
    /// clock.
    /// </summary>
    internal sealed class EnemyMotion
    {
        private readonly MotionClock _clock = new MotionClock();
        private float _nextLog;

        /// <summary>
        /// For the map test (nl-motion): how often states come and how
        /// smoothly enemies are drawn.
        /// </summary>
        public MotionStats Stats => _clock.Stats;

        /// <summary>The map test's line on the arrivals and motion.</summary>
        public string Summary() => Stats.Summary(_clock);

        /// <summary>
        /// A batch of states came, sent at the host's time
        /// <paramref name="sent"/>.
        /// </summary>
        public void Arrived(float sent, float now)
        {
            _clock.Arrived(new Seconds(sent), new Seconds(now));
        }

        /// <summary>
        /// Called once a frame before the enemies are drawn: the delay moves
        /// towards what the arrivals need.
        /// </summary>
        public void Frame(float dt)
        {
            _clock.Frame(new Seconds(dt));
            if (World.WorldActions.Debug && Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 30f;
                float delay = _clock.Delay.Value * 1000f;
                float interval = _clock.Interval.Value * 1000f;
                float jitter = _clock.Jitter.Value * 1000f;
                Plugin.Log.LogInfo(
                    FormattableString.Invariant(
                            $"[enemy] drawn {delay:0} ms ")
                        + "behind the host "
                        + FormattableString.Invariant(
                            $"(interval {interval:0} ms, ")
                        + FormattableString.Invariant(
                            $"arrivals vary by {jitter:0} ms)"));
            }
        }

        /// <summary>A state for one enemy.</summary>
        public static void Add(
            MotionTrack track, float sent, Vector3 pos, Quaternion rot)
        {
            track.Add(new Seconds(sent), VecOf(pos), FacingOf(rot));
        }

        /// <summary>
        /// Where the enemy is drawn now. False when there is no state yet.
        /// </summary>
        public bool Where(
            MotionTrack track, float now, out Vector3 pos, out Quaternion rot)
        {
            var at = _clock.DrawnAt(new Seconds(now));
            bool has = track.Where(at, out var pose);
            pos = VectorOf(pose.Pos);
            rot = QuaternionOf(pose.From);
            if (pose.Blends)
            {
                rot = Quaternion.Slerp(rot, QuaternionOf(pose.To), pose.Blend);
            }

            if (!has)
            {
                pos = default;
                rot = Quaternion.identity;
            }

            return has;
        }

        /// <summary>An enemy was drawn this frame at pos.</summary>
        public void Drawn(MotionTrack track, Vector3 pos, float dt)
        {
            Stats.Drawn(track, VecOf(pos), new Seconds(dt));
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private static Facing FacingOf(Quaternion q) =>
            new Facing(q.x, q.y, q.z, q.w);

        private static Quaternion QuaternionOf(Facing f) =>
            new Quaternion(f.X, f.Y, f.Z, f.W);
    }
}
