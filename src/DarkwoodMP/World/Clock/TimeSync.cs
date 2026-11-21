using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Clock;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// One clock for everyone: the host's. The host sends its day and time
    /// (TimeSync: u32 day, u16 minute) every second. A client's own clock
    /// doesn't run (Controller.DoUpdateTime off); it steps to the host's a
    /// minute at a time, calling the game's refreshTime() for each, so dusk,
    /// night, dawn and their events come as they would have (a big jump, such
    /// as the host sleeping, goes straight there).
    ///
    /// Dreams: the game stops the clock in a dream (Controller.FixedUpdate) and
    /// sets CurrentTime to the dream's own time, keeping the world's in
    /// Dreams.timeCopy until waking. So a dreaming host sends timeCopy, and the
    /// world's clock stands still for everyone until the host wakes, as it does
    /// in the game. A dream outcome with a custom end time (DreamPreset outcome
    /// customEndTime) sets the host's clock on waking, and that becomes
    /// everyone's world time on purpose: the host decides it. A dreaming
    /// joiner's dream time is its own: it doesn't step while dreaming, and on
    /// waking (when the game puts its clock back to timeCopy) the next TimeSync
    /// steps it to the host's time, running the minutes it missed.
    ///
    /// One lives for each session, in CoopSession.Time.
    /// </summary>
    internal sealed class TimeSync
    {
        private readonly Session _session;
        private float _nextSend;
        private bool _clockStopped;
        private bool _dreamLogged;

        public TimeSync(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.TimeSync, OnTime);
            coop.Hook(RestartClock);
        }

        /// The session ended: a clock we kept off runs again, as it would
        /// in the first frame after (we are no longer a client's copy).
        private void RestartClock()
        {
            if (!_clockStopped)
            {
                return;
            }

            _clockStopped = false;
            var ctl = Singleton<Controller>.Instance;
            if (ctl != null)
            {
                ctl.DoUpdateTime = true;
            }
        }

        public void Update()
        {
            var ctl = Singleton<Controller>.Instance;
            ClockSendFacts send = FactsOf(ctl);
            if (ClockRules.MaySend(send))
            {
                _nextSend = ClockRules.NextSend(send.Now).Value;
                var w = _session.Begin(MsgType.TimeSync);
                int minute = ClockRules.MinuteToSend(send);
                TimeSyncMessage.Write(
                    w,
                    new HostClock { Day = ctl.day, Minute = minute });
                _session.Broadcast(w, Channel.Control, reliable: true);
            }

            ClockHold hold = ClockRules.OwnClock(
                NetEnemies.ClientMode,
                ctl != null,
                _clockStopped);
            if (hold == ClockHold.Stop)
            {
                if (ctl.DoUpdateTime)
                {
                    // the game turns it back on at times; keep it off
                    ctl.DoUpdateTime = false;
                }

                _clockStopped = true;
            }
            else if (hold == ClockHold.Restart)
            {
                _clockStopped = false;
                ctl.DoUpdateTime = true;
            }
            else if (hold == ClockHold.Forget)
            {
                _clockStopped = false;
            }
        }

        /// What the host's clock is, to send it. Dreaming: the clock shows
        /// the dream's time, and the world's waits in timeCopy.
        private ClockSendFacts FactsOf(Controller ctl)
        {
            var dreams = Singleton<Dreams>.Instance;
            bool dreaming = dreams != null && dreams.dreaming;
            float timeCopy = 0f;
            if (dreaming)
            {
                timeCopy = dreams.timeCopy;
            }

            int minute = 0;
            if (ctl != null)
            {
                minute = ctl.CurrentTime;
            }

            return new ClockSendFacts
            {
                IsHost = _session.IsHost,
                Peers = _session.Peers.Count,
                InWorld = WorldShare.InWorld,
                HasController = ctl != null,
                Now = new Seconds(Time.unscaledTime),
                NextSend = new Seconds(_nextSend),
                Dreaming = dreaming,
                TimeCopy = timeCopy,
                Minute = minute,
            };
        }

        /// What a client's clock is when the host's comes.
        private static ClockFacts FactsOf(Controller ctl, HostClock host)
        {
            var dreams = Singleton<Dreams>.Instance;
            bool dreaming = dreams != null
                && (dreams.dreaming || dreams.switchingDream);
            int day = 0;
            int minute = 0;
            if (ctl != null)
            {
                day = ctl.day;
                minute = ctl.CurrentTime;
            }

            return new ClockFacts
            {
                HasController = ctl != null,
                InWorld = WorldShare.InWorld,
                Dreaming = dreaming,
                HostDay = host.Day,
                HostMinute = host.Minute,
                Day = day,
                Minute = minute,
            };
        }

        private void OnTime(ulong from, PacketReader r)
        {
            bool fromHost = from == _session.HostId;
            if (!ClockRules.Takes(fromHost, NetEnemies.ClientMode))
            {
                return;
            }

            HostClock host = TimeSyncMessage.Read(r);
            var ctl = Singleton<Controller>.Instance;
            ClockStep step = ClockRules.CatchUp(FactsOf(ctl, host));
            if (step.Move == ClockMove.Ignore)
            {
                return;
            }

            if (step.Move == ClockMove.Wait)
            {
                // The clock shows the dream's time; the world's waits in
                // timeCopy, and catches up after waking.
                if (WorldActions.Debug && !_dreamLogged)
                {
                    var dreams = Singleton<Dreams>.Instance;
                    Plugin.Log.LogInfo(
                        $"[world] dreaming: the clock waits (host at day "
                            + $"{host.Day} {host.Minute}, world time kept "
                            + $"{dreams.timeCopy:0})");
                }

                _dreamLogged = true;
                return;
            }

            _dreamLogged = false;
            if (step.Move == ClockMove.Nothing)
            {
                return;
            }

            if (step.Move == ClockMove.Step)
            {
                Step(ctl, host.Day, step.Minutes);
                return;
            }

            // Far off (joining late, the host slept): go straight there.
            ctl.setDay(host.Day);
            ctl.CurrentTime = host.Minute;
            ctl.refreshTime(afterGameLoad: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] clock jumped to day {ctl.day} {ctl.CurrentTime}");
            }
        }

        /// Steps the clock a minute at a time, so dusk, night, dawn and
        /// their events come as they would have.
        private static void Step(Controller ctl, int day, int behind)
        {
            for (int i = 0; i < behind; i++)
            {
                ctl.CurrentTime++;
                // wraps at 1440; at dawn startDay() counts the new day
                ctl.refreshTime();
            }

            if (ctl.day != day)
            {
                ctl.setDay(day);
            }

            if (WorldActions.Debug && behind > 1)
            {
                Plugin.Log.LogInfo(
                    $"[world] clock stepped {behind} minutes to day "
                        + $"{ctl.day} {ctl.CurrentTime}");
            }
        }
    }
}
