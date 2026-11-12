using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: how the host's enemies are moved between the host's states.
    ///
    /// Each EnemyState carries the host's clock (Time.unscaledTime) when it was sent. A client keeps each enemy's
    /// last few states by that time, and every frame draws the enemy where the host has it now, as far as the states
    /// tell: between the two states around that moment when the next one is already here, or else carried on from
    /// the last one at the speed between the last two, for up to MaxAhead. The host sends about every 50-70 ms
    /// (StateInterval, rounded up to its frames), and states come late or in bunches (TCP, the host's or our own
    /// slow frames, a lost one on Steam's unreliable channel), so drawing from the latest arrival, as before, made
    /// enemies stop and jump. With the host's times the speed is the host's real speed, whenever the states came.
    ///
    /// The clock: the gap between our clock and the host's at each arrival is the host's clock offset plus how long
    /// the state took to come. The smallest gaps are the quickest arrivals; Offset follows them (it drops at once
    /// to a quicker one and rises slowly). Delay, how far behind that the enemies are drawn, is at most MaxDelay
    /// (30 ms; half of how much arrivals vary).
    ///
    /// Why not further behind, which is smoother (every frame between two real states): a copy of the host's enemy
    /// is solid here, and our player can't walk through it. Drawn 0.1-0.3 s behind the host, as at first, a dog
    /// coming at our player stood well short of where it was on the host, so our player walked up to the copy while
    /// its stand-in on the host was already inside the dog's bite and bumping into it (sleeping and eating dogs
    /// woke). In the map test the players were bitten to death by dogs they kept clear of when drawn by the latest
    /// state. Drawn at the host's present instead, a turning enemy is a little off until the next state comes.
    /// </summary>
    public static class EnemyMotion
    {
        public struct Sample { public float T; public Vector3 Pos; public Quaternion Rot; }

        /// <summary>One enemy's states by the host's time, oldest first.</summary>
        public sealed class Track
        {
            public readonly List<Sample> Samples = new List<Sample>(MaxSamples);
            public Vector3 Drawn; public bool HasDrawn; // where it was drawn last frame (for the stats)
            public float Speed; // the host's speed between the two states it's drawn between (for the stats)
        }

        private const int MaxSamples = 12;
        /// <summary>A state this long after the last (host time) starts the enemy afresh where it is: it was out of range, or switched off.</summary>
        private const float Gap = 0.75f;
        /// <summary>A move this far between two states is a teleport: drawn at once, not slid.</summary>
        private const float Teleport = 600f;
        /// <summary>How far past its last state an enemy is carried on at its speed.</summary>
        private const float MaxAhead = 0.15f;
        private const float MinDelay = 0f, MaxDelay = 0.03f;

        private static bool _hasClock;
        private static float _offset, _lastSent = -1f, _interval = NetEnemies.StateInterval, _jitter, _delay = 0f;

        public static float Delay => _delay;

        public static void Reset()
        {
            _hasClock = false; _lastSent = -1f; _interval = NetEnemies.StateInterval; _jitter = 0f; _delay = 0f;
            Stats.Reset();
        }

        /// <summary>A batch of states came, sent at the host's time <paramref name="sent"/>.</summary>
        public static void Arrived(float sent, float now)
        {
            float gap = now - sent;
            if (!_hasClock || Mathf.Abs(gap - _offset) > 2f) { _offset = gap; _hasClock = true; _jitter = 0f; }
            else if (gap < _offset) _offset = gap;
            else _offset += (gap - _offset) * 0.01f; // so a lasting rise in latency is followed, in a few seconds
            _jitter += (Mathf.Abs(gap - _offset) - _jitter) * 0.05f;
            if (_lastSent >= 0f && sent > _lastSent && sent - _lastSent < 0.5f)
                _interval += (sent - _lastSent - _interval) * 0.1f;
            _lastSent = sent;
            Stats.Batch(sent, now);
        }

        /// <summary>Called once a frame before the enemies are drawn: Delay moves towards what the arrivals need.</summary>
        public static void Frame(float dt)
        {
            float want = Mathf.Clamp(_jitter * 0.5f, MinDelay, MaxDelay);
            _delay = Mathf.MoveTowards(_delay, want, 0.05f * dt); // at most 5% faster or slower, so it isn't seen
            Stats.Frame(dt);
            if (World.WorldActions.Debug && Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 30f;
                Plugin.Log.LogInfo(FormattableString.Invariant($"[enemy] drawn {_delay * 1000f:0} ms behind the host (interval {_interval * 1000f:0} ms, arrivals vary by {_jitter * 1000f:0} ms)"));
            }
        }
        private static float _nextLog;

        /// <summary>A state for one enemy.</summary>
        public static void Add(Track t, float sent, Vector3 pos, Quaternion rot)
        {
            var s = t.Samples;
            if (s.Count > 0)
            {
                var last = s[s.Count - 1];
                if (sent - last.T > Gap || (pos - last.Pos).sqrMagnitude > Teleport * Teleport) s.Clear();
                else if (sent <= last.T) { s[s.Count - 1] = new Sample { T = last.T, Pos = pos, Rot = rot }; return; } // the same moment again (a corpse's state)
            }
            if (s.Count == MaxSamples) s.RemoveAt(0);
            s.Add(new Sample { T = sent, Pos = pos, Rot = rot });
        }

        /// <summary>Where the enemy is drawn now. False when there is no state yet.</summary>
        public static bool Where(Track t, float now, out Vector3 pos, out Quaternion rot)
        {
            var s = t.Samples;
            pos = default; rot = Quaternion.identity;
            if (s.Count == 0) return false;
            float at = now - _offset - _delay;
            while (s.Count > 2 && s[1].T <= at) s.RemoveAt(0); // keep the one before the moment drawn
            var a = s[0];
            if (s.Count == 1 || at <= a.T) { pos = a.Pos; rot = a.Rot; t.Speed = 0f; return true; }
            for (int i = 0; i + 1 < s.Count; i++)
            {
                var b = s[i + 1];
                if (at > b.T) { a = b; continue; }
                float k = (at - a.T) / Mathf.Max(0.001f, b.T - a.T);
                pos = Vector3.Lerp(a.Pos, b.Pos, k); rot = Quaternion.Slerp(a.Rot, b.Rot, k);
                t.Speed = Flat(b.Pos - a.Pos) / Mathf.Max(0.001f, b.T - a.T);
                return true;
            }
            // Past the last state: on at its last speed for a moment.
            var last = s[s.Count - 1]; var prev = s[s.Count - 2];
            float span = last.T - prev.T;
            float ahead = Mathf.Min(at - last.T, MaxAhead);
            bool carry = span > 0.001f && span < 0.3f;
            pos = carry ? last.Pos + (last.Pos - prev.Pos) * (ahead / span) : last.Pos;
            t.Speed = carry && at - last.T < MaxAhead ? Flat(last.Pos - prev.Pos) / span : 0f;
            rot = last.Rot;
            return true;
        }

        private static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

        /// <summary>
        /// For the map test (nl-motion): how often states come and how smoothly enemies are drawn. Only counted while
        /// On. A "stall" is a frame where an enemy the host has moving at 40 or more a second moves less than a
        /// quarter of what it should (by the host's speed between the two states it's drawn between); a "jump", one
        /// where it moves more than two and a half times that.
        /// </summary>
        public static class Stats
        {
            public static bool On;
            public static int Batches, Frames, Moving, Stalls, Jumps, LateGaps;
            public static float MaxGap, MaxHostGap, MaxFrame, SumGap, SumErr;
            private static float _lastNow = -1f, _lastSent = -1f;
            private static readonly List<float> _gaps = new List<float>();

            public static void Reset()
            {
                Batches = Frames = Moving = Stalls = Jumps = LateGaps = 0;
                MaxGap = MaxHostGap = MaxFrame = SumGap = SumErr = 0f;
                _lastNow = _lastSent = -1f; _gaps.Clear();
            }

            internal static void Batch(float sent, float now)
            {
                if (!On) return;
                Batches++;
                if (_lastNow >= 0f)
                {
                    float g = now - _lastNow; _gaps.Add(g); SumGap += g;
                    if (g > MaxGap) MaxGap = g;
                    if (g > 0.15f) LateGaps++;
                    float hg = sent - _lastSent; if (hg > MaxHostGap) MaxHostGap = hg;
                }
                _lastNow = now; _lastSent = sent;
            }

            internal static void Frame(float dt) { if (!On) return; Frames++; if (dt > MaxFrame) MaxFrame = dt; }

            /// <summary>An enemy was drawn this frame at pos.</summary>
            internal static void Drawn(Track t, Vector3 pos, float dt)
            {
                if (!On) { t.HasDrawn = false; return; }
                if (t.HasDrawn && dt > 0.0001f)
                {
                    float speed = t.Speed;
                    if (speed >= 40f && speed < 2000f)
                    {
                        float want = speed * dt, got = Flat(pos - t.Drawn);
                        Moving++;
                        if (got < want * 0.25f) Stalls++;
                        else if (got > want * 2.5f) Jumps++;
                        SumErr += Mathf.Min(3f, Mathf.Abs(got / want - 1f));
                    }
                }
                t.Drawn = pos; t.HasDrawn = true;
            }

            public static string Summary()
            {
                _gaps.Sort();
                float p95 = _gaps.Count > 0 ? _gaps[Mathf.Min(_gaps.Count - 1, (int)(_gaps.Count * 0.95f))] : 0f;
                return FormattableString.Invariant(
                    $"{Batches} batches, gap avg {(_gaps.Count > 0 ? SumGap / _gaps.Count * 1000f : 0f):0} ms p95 {p95 * 1000f:0} max {MaxGap * 1000f:0} (over 150 ms: {LateGaps}; host's max {MaxHostGap * 1000f:0}); ") + FormattableString.Invariant(
                    $"{Frames} frames, slowest {MaxFrame * 1000f:0} ms; moving enemy frames {Moving}: stalls {Stalls} ({Pct(Stalls)}), jumps {Jumps} ({Pct(Jumps)}), ") + FormattableString.Invariant(
                    $"speed error avg {(Moving > 0 ? SumErr / Moving * 100f : 0f):0}%; delay {_delay * 1000f:0} ms (interval {_interval * 1000f:0}, jitter {_jitter * 1000f:0})");
            }

            private static string Pct(int n) => Moving > 0 ? FormattableString.Invariant($"{n * 100f / Moving:0.0}%") : "-";
        }
    }
}
