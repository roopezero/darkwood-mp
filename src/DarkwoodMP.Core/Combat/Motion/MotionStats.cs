using System;
using System.Collections.Generic;

namespace DarkwoodMP.Combat.Motion
{
    /// For the map test (nl-motion): how often states come and how
    /// smoothly enemies are drawn (the shell's EnemyMotion.Stats). Counted
    /// only while On. A stall is a frame where an enemy the host has
    /// moving at MinSpeed or more moves less than a quarter of what it
    /// should (by the host's speed between the two states it is drawn
    /// between); a jump, one where it moves more than two and a half times
    /// that.
    public sealed class MotionStats
    {
        /// The slowest host speed a frame counts as moving at.
        public const float MinSpeed = 40f;

        /// Host speeds from this on aren't counted (a teleport).
        public const float MaxSpeed = 2000f;

        /// Under this share of the expected move is a stall.
        public const float StallShare = 0.25f;

        /// Over this share of the expected move is a jump.
        public const float JumpShare = 2.5f;

        /// The most one frame's speed error counts.
        public const float MaxError = 3f;

        /// A gap between arrivals longer than this is late.
        public const float LateGap = 0.15f;

        /// A frame shorter than this isn't measured.
        public const float MinFrame = 0.0001f;

        float _lastNow = -1f;
        float _lastSent = -1f;
        readonly List<float> _gaps = new List<float>();

        public bool On { get; set; }

        public int Batches { get; private set; }

        public int Frames { get; private set; }

        public int Moving { get; private set; }

        public int Stalls { get; private set; }

        public int Jumps { get; private set; }

        public int LateGaps { get; private set; }

        public float MaxGap { get; private set; }

        public float MaxHostGap { get; private set; }

        public float MaxFrame { get; private set; }

        public float SumGap { get; private set; }

        public float SumErr { get; private set; }

        public void Reset()
        {
            Batches = 0;
            Frames = 0;
            Moving = 0;
            Stalls = 0;
            Jumps = 0;
            LateGaps = 0;
            MaxGap = 0f;
            MaxHostGap = 0f;
            MaxFrame = 0f;
            SumGap = 0f;
            SumErr = 0f;
            _lastNow = -1f;
            _lastSent = -1f;
            _gaps.Clear();
        }

        internal void Batch(Seconds sent, Seconds now)
        {
            if (!On)
            {
                return;
            }

            Batches++;
            if (_lastNow >= 0f)
            {
                float g = now.Value - _lastNow;
                _gaps.Add(g);
                SumGap += g;
                if (g > MaxGap)
                {
                    MaxGap = g;
                }

                if (g > LateGap)
                {
                    LateGaps++;
                }

                float hg = sent.Value - _lastSent;
                if (hg > MaxHostGap)
                {
                    MaxHostGap = hg;
                }
            }

            _lastNow = now.Value;
            _lastSent = sent.Value;
        }

        internal void Frame(Seconds dt)
        {
            if (!On)
            {
                return;
            }

            Frames++;
            if (dt.Value > MaxFrame)
            {
                MaxFrame = dt.Value;
            }
        }

        /// An enemy was drawn this frame at pos, after its track's Where.
        public void Drawn(MotionTrack track, Vec3 pos, Seconds dt)
        {
            if (!On)
            {
                track.HasDrawn = false;
                return;
            }

            if (track.HasDrawn && dt.Value > MinFrame)
            {
                Measure(track, pos, dt.Value);
            }

            track.Drawn = pos;
            track.HasDrawn = true;
        }

        void Measure(MotionTrack track, Vec3 pos, float dt)
        {
            float speed = track.Speed;
            if (speed < MinSpeed || speed >= MaxSpeed)
            {
                return;
            }

            float want = speed * dt;
            float got = MotionMath.Flat(pos, track.Drawn);
            Moving++;
            if (got < want * StallShare)
            {
                Stalls++;
            }
            else if (got > want * JumpShare)
            {
                Jumps++;
            }

            float err = MotionMath.Abs(got / want - 1f);
            SumErr += MotionMath.Min(MaxError, err);
        }

        /// The line the map test logs, with the clock's delay, interval
        /// and jitter. Sorts the gaps it has.
        public string Summary(MotionClock clock)
        {
            _gaps.Sort();
            float p95 = 0f;
            float avg = 0f;
            if (_gaps.Count > 0)
            {
                int at = (int)(_gaps.Count * 0.95f);
                p95 = _gaps[Math.Min(_gaps.Count - 1, at)];
                avg = SumGap / _gaps.Count * 1000f;
            }

            float error = 0f;
            if (Moving > 0)
            {
                error = SumErr / Moving * 100f;
            }

            float delay = clock.Delay.Value * 1000f;
            float interval = clock.Interval.Value * 1000f;
            float jitter = clock.Jitter.Value * 1000f;
            return FormattableString.Invariant(
                    $"{Batches} batches, gap avg {avg:0} ms p95 ")
                + FormattableString.Invariant(
                    $"{p95 * 1000f:0} max {MaxGap * 1000f:0} (over 150 ms: ")
                + FormattableString.Invariant(
                    $"{LateGaps}; host's max {MaxHostGap * 1000f:0}); ")
                + FormattableString.Invariant(
                    $"{Frames} frames, slowest {MaxFrame * 1000f:0} ms; ")
                + FormattableString.Invariant(
                    $"moving enemy frames {Moving}: stalls {Stalls} ")
                + FormattableString.Invariant(
                    $"({Pct(Stalls)}), jumps {Jumps} ({Pct(Jumps)}), ")
                + FormattableString.Invariant(
                    $"speed error avg {error:0}%; delay {delay:0} ms ")
                + FormattableString.Invariant(
                    $"(interval {interval:0}, jitter {jitter:0})");
        }

        string Pct(int n)
        {
            if (Moving <= 0)
            {
                return "-";
            }

            return FormattableString.Invariant($"{n * 100f / Moving:0.0}%");
        }
    }
}
