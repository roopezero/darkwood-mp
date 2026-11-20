namespace DarkwoodMP.Combat.Motion
{
    /// Client: the host's clock as the enemy states tell it, and how far
    /// behind it the host's enemies are drawn (the shell's EnemyMotion
    /// statics: Arrived, Frame, Delay, Reset).
    ///
    /// The gap between our clock and the host's at each arrival is the
    /// host's clock offset plus how long the state took to come. The
    /// smallest gaps are the quickest arrivals; the offset follows them
    /// (it drops at once to a quicker one and rises slowly). The delay,
    /// how far behind that the enemies are drawn, is at most MaxDelay:
    /// half of how much arrivals vary.
    public sealed class MotionClock
    {
        /// The furthest behind the host's present that enemies are drawn.
        public const float MaxDelay = 0.03f;

        /// How often the host sends its enemies' states
        /// (NetEnemies.StateInterval): the interval a clock starts from.
        public const float StateInterval = 0.05f;

        /// A gap this far from the offset is a new clock: taken at once.
        public const float Resync = 2f;

        /// The share of a slower gap the offset rises by, so a lasting
        /// rise in latency is followed in a few seconds.
        public const float OffsetRise = 0.01f;

        /// The share of each change the jitter follows.
        public const float JitterRate = 0.05f;

        /// The share of each change the interval follows.
        public const float IntervalRate = 0.1f;

        /// The longest step between two batches the interval is taken
        /// from.
        public const float LongestStep = 0.5f;

        /// How fast the delay moves, a share of each frame's time: at most
        /// 5% faster or slower, so it isn't seen.
        public const float DelayRate = 0.05f;

        bool _hasClock;
        float _offset;
        float _lastSent = -1f;
        float _interval = StateInterval;
        float _jitter;
        float _delay;

        /// The map test's counters, fed by Arrived and Frame.
        public MotionStats Stats { get; } = new MotionStats();

        /// How far behind the host's present the enemies are drawn.
        public Seconds Delay => new Seconds(_delay);

        /// The host's clock offset plus the quickest arrival's time.
        public Seconds Offset => new Seconds(_offset);

        /// How often batches come, by the host's times.
        public Seconds Interval => new Seconds(_interval);

        /// How much arrivals vary.
        public Seconds Jitter => new Seconds(_jitter);

        /// A new session or world: no clock yet. Keeps the old offset,
        /// which the next arrival replaces.
        public void Reset()
        {
            _hasClock = false;
            _lastSent = -1f;
            _interval = StateInterval;
            _jitter = 0f;
            _delay = 0f;
            Stats.Reset();
        }

        /// A batch of states came at our time now, sent at the host's
        /// time sent.
        public void Arrived(Seconds sent, Seconds now)
        {
            float at = sent.Value;
            float gap = now.Value - at;
            if (!_hasClock || MotionMath.Abs(gap - _offset) > Resync)
            {
                _offset = gap;
                _hasClock = true;
                _jitter = 0f;
            }
            else if (gap < _offset)
            {
                _offset = gap;
            }
            else
            {
                _offset += (gap - _offset) * OffsetRise;
            }

            float off = MotionMath.Abs(gap - _offset);
            _jitter += (off - _jitter) * JitterRate;
            float step = at - _lastSent;
            if (_lastSent >= 0f && at > _lastSent && step < LongestStep)
            {
                _interval += (step - _interval) * IntervalRate;
            }

            _lastSent = at;
            Stats.Batch(sent, now);
        }

        /// Once a frame before the enemies are drawn, with the frame's
        /// time: the delay moves towards what the arrivals need.
        public void Frame(Seconds dt)
        {
            float want = MotionMath.Min(_jitter * 0.5f, MaxDelay);
            float most = DelayRate * dt.Value;
            _delay = MotionMath.MoveTowards(_delay, want, most);
            Stats.Frame(dt);
        }

        /// The host's time to draw the enemies at, at our time now.
        public Seconds DrawnAt(Seconds now) =>
            new Seconds(now.Value - _offset - _delay);
    }
}
