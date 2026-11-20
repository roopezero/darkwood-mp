using System.Collections.Generic;

namespace DarkwoodMP.Combat.Motion
{
    /// One host enemy's last few states on a client, by the host's clock,
    /// oldest first, and where it is drawn between them (the shell's
    /// EnemyMotion.Track, Add and Where). The client keeps one per copy.
    public sealed class MotionTrack
    {
        /// The most states kept.
        public const int MaxSamples = 12;

        /// A state this long after the last (host time) starts the enemy
        /// afresh where it is: it was out of range, or switched off.
        public const float Gap = 0.75f;

        /// A move this far between two states is a teleport: drawn at
        /// once, not slid.
        public const float Teleport = 600f;

        /// How far past its last state an enemy is carried on at its speed.
        public const float MaxAhead = 0.15f;

        /// The shortest span between two states that a speed is taken
        /// from, and that a blend divides by.
        public const float MinSpan = 0.001f;

        /// The longest span between the last two states that the enemy is
        /// carried on from.
        public const float MaxCarrySpan = 0.3f;

        readonly List<MotionSample> _samples =
            new List<MotionSample>(MaxSamples);

        public IReadOnlyList<MotionSample> Samples => _samples;

        /// Where it was drawn last frame, when HasDrawn (for the stats).
        public Vec3 Drawn { get; internal set; }

        public bool HasDrawn { get; internal set; }

        /// The host's speed between the two states it is drawn between
        /// (for the stats), set by Where.
        public float Speed { get; private set; }

        /// A state for this enemy, sent at the host's time sent.
        public void Add(Seconds sent, Vec3 pos, Facing rot)
        {
            var s = _samples;
            if (s.Count > 0)
            {
                var last = s[s.Count - 1];
                bool late = sent.Value - last.Sent.Value > Gap;
                float jump = MotionMath.SqrDistance(pos, last.Pos);
                if (late || jump > Teleport * Teleport)
                {
                    s.Clear();
                }
                else if (sent.Value <= last.Sent.Value)
                {
                    // The same moment again (a corpse's state).
                    s[s.Count - 1] = new MotionSample(last.Sent, pos, rot);
                    return;
                }
            }

            if (s.Count == MaxSamples)
            {
                s.RemoveAt(0);
            }

            s.Add(new MotionSample(sent, pos, rot));
        }

        /// Where the enemy is drawn at the host's time at (the clock's
        /// DrawnAt). False when there is no state yet. Drops the states
        /// older than the one before that moment.
        public bool Where(Seconds at, out MotionPose pose)
        {
            var s = _samples;
            float now = at.Value;
            pose = new MotionPose
            {
                From = Facing.Identity,
            };

            if (s.Count == 0)
            {
                return false;
            }

            while (s.Count > 2 && s[1].Sent.Value <= now)
            {
                // Keep the one before the moment drawn.
                s.RemoveAt(0);
            }

            var a = s[0];
            if (s.Count == 1 || now <= a.Sent.Value)
            {
                pose = new MotionPose
                {
                    Pos = a.Pos,
                    From = a.Rot,
                };

                Speed = 0f;
                return true;
            }

            for (int i = 0; i + 1 < s.Count; i++)
            {
                var b = s[i + 1];
                if (now > b.Sent.Value)
                {
                    a = b;
                    continue;
                }

                float span = MotionMath.Max(
                    MinSpan,
                    b.Sent.Value - a.Sent.Value);
                float k = (now - a.Sent.Value) / span;
                pose = new MotionPose
                {
                    Pos = MotionMath.Lerp(a.Pos, b.Pos, k),
                    From = a.Rot,
                    To = b.Rot,
                    Blend = Clamp01(k),
                    Blends = true,
                };

                Speed = MotionMath.Flat(b.Pos, a.Pos) / span;
                return true;
            }

            pose = Carried(now);
            return true;
        }

        /// Past the last state: on at its last speed for a moment.
        MotionPose Carried(float now)
        {
            var s = _samples;
            var last = s[s.Count - 1];
            var prev = s[s.Count - 2];
            float span = last.Sent.Value - prev.Sent.Value;
            float ahead = MotionMath.Min(now - last.Sent.Value, MaxAhead);
            bool carry = span > MinSpan && span < MaxCarrySpan;
            var pos = last.Pos;
            if (carry)
            {
                pos = MotionMath.Beyond(last.Pos, prev.Pos, ahead / span);
            }

            Speed = 0f;
            if (carry && now - last.Sent.Value < MaxAhead)
            {
                Speed = MotionMath.Flat(last.Pos, prev.Pos) / span;
            }

            return new MotionPose
            {
                Pos = pos,
                From = last.Rot,
            };
        }

        /// As Quaternion.Slerp holds its t: 0 to 1.
        static float Clamp01(float t)
        {
            if (t < 0f)
            {
                return 0f;
            }

            if (t > 1f)
            {
                return 1f;
            }

            return t;
        }
    }
}
