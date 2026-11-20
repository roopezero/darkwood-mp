using System;

namespace DarkwoodMP.World.Clock
{
    /// One clock for everyone, the host's (the plugin's TimeSync). The host
    /// sends its day and minute every second; a client's own clock is
    /// stopped and steps to the host's a minute at a time, or jumps there
    /// when it is far off.
    public static class ClockRules
    {
        /// Seconds between two TimeSync messages (unscaled).
        public const float SendEvery = 1f;

        /// The most minutes a client steps for one TimeSync; further off
        /// it jumps.
        public const int MaxSteps = 120;

        /// The most days apart a client steps instead of jumping.
        public const int MaxDaysApart = 1;

        public const int MinutesPerDay = 1440;

        /// The last minute of a day.
        public const int LastMinute = MinutesPerDay - 1;

        /// Host: whether a TimeSync goes now. Then the next is due at
        /// NextSend(now).
        public static bool MaySend(in ClockSendFacts clock)
        {
            return clock.IsHost
                && clock.Peers > 0
                && clock.InWorld
                && clock.HasController
                && clock.Now.Value >= clock.NextSend.Value;
        }

        /// When the TimeSync after one sent now is due.
        public static Seconds NextSend(Seconds now)
        {
            return new Seconds(now.Value + SendEvery);
        }

        /// The minute the host sends: the world's while it dreams
        /// (timeCopy, whole minutes, clamped to the day), else the
        /// clock's as it is.
        public static int MinuteToSend(in ClockSendFacts clock)
        {
            if (clock.Dreaming)
            {
                return Clamp((int)clock.TimeCopy, 0, LastMinute);
            }

            return clock.Minute;
        }

        /// Client, each frame: what to do with our own clock. It is kept
        /// off while we are a client's copy (NetEnemies.ClientMode) and
        /// turned on once, in the first frame we aren't.
        public static ClockHold OwnClock(
            bool clientMode,
            bool hasController,
            bool stopped)
        {
            if (clientMode && hasController)
            {
                return ClockHold.Stop;
            }

            if (!stopped)
            {
                return ClockHold.Leave;
            }

            if (hasController)
            {
                return ClockHold.Restart;
            }

            return ClockHold.Forget;
        }

        /// Whether a TimeSync is read: from the host, while we are a
        /// client's copy.
        public static bool Takes(bool fromHost, bool clientMode)
        {
            return fromHost && clientMode;
        }

        /// How a client catches up with the host's clock.
        public static ClockStep CatchUp(in ClockFacts clock)
        {
            if (!clock.HasController || !clock.InWorld)
            {
                return new ClockStep { Move = ClockMove.Ignore };
            }

            if (clock.Dreaming)
            {
                return new ClockStep { Move = ClockMove.Wait };
            }

            int behind = Behind(clock.HostMinute, clock.Minute);
            if (behind == 0 && clock.Day == clock.HostDay)
            {
                return new ClockStep { Move = ClockMove.Nothing };
            }

            if (behind <= MaxSteps
                && Math.Abs(clock.HostDay - clock.Day) <= MaxDaysApart)
            {
                return new ClockStep
                {
                    Move = ClockMove.Step,
                    Minutes = behind,
                };
            }

            return new ClockStep { Move = ClockMove.Jump };
        }

        /// Minutes from ours to the host's minute, round the clock, 0 to
        /// LastMinute. Darkwood counts a new day at dawn, so the minute of
        /// the day is what only goes forward. A client ahead by one minute
        /// is 1439 behind.
        public static int Behind(int host, int ours)
        {
            return ((host - ours) % MinutesPerDay + MinutesPerDay)
                % MinutesPerDay;
        }

        /// As Mathf.Clamp.
        static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
