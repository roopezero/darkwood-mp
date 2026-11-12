using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// One clock for everyone: the host's. The host sends its day and time (TimeSync: u32 day, u16 minute) every
    /// second. A client's own clock doesn't run (Controller.DoUpdateTime off); it steps to the host's a minute at a
    /// time, calling the game's refreshTime() for each, so dusk, night, dawn and their events come as they would
    /// have (a big jump, such as the host sleeping, goes straight there).
    ///
    /// Dreams: the game stops the clock in a dream (Controller.FixedUpdate) and sets CurrentTime to the dream's own
    /// time, keeping the world's in Dreams.timeCopy until waking. So a dreaming host sends timeCopy, and the world's
    /// clock stands still for everyone until the host wakes, as it does in the game. A dream outcome with a custom
    /// end time (DreamPreset outcome customEndTime) sets the host's clock on waking, and that becomes everyone's
    /// world time on purpose: the host decides it. A dreaming joiner's dream time is its own: it doesn't step while
    /// dreaming, and on waking (when the game puts its clock back to timeCopy) the next TimeSync steps it to the
    /// host's time, running the minutes it missed.
    /// </summary>
    public sealed class TimeSync
    {
        private const int MaxStepsPerUpdate = 120;
        private readonly Session _session;
        private float _nextSend;
        private bool _clockStopped;
        private bool _dreamLogged;

        public TimeSync(Session session)
        {
            _session = session;
            session.Register(MsgType.TimeSync, OnTime);
        }

        public void Update()
        {
            var ctl = Singleton<Controller>.Instance;
            if (_session.IsHost && _session.Peers.Count > 0 && WorldShare.InWorld && ctl != null && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 1f;
                var w = _session.Begin(MsgType.TimeSync);
                var dreams = Singleton<Dreams>.Instance;
                bool dreaming = dreams != null && dreams.dreaming;
                w.W.Write((uint)ctl.day);
                w.W.Write((ushort)(dreaming ? Mathf.Clamp((int)dreams.timeCopy, 0, 1439) : ctl.CurrentTime));
                _session.Broadcast(w, Channel.Control, reliable: true);
            }

            bool client = NetEnemies.ClientMode && ctl != null;
            if (client)
            {
                if (ctl.DoUpdateTime) ctl.DoUpdateTime = false; // the game turns it back on at times; keep it off
                _clockStopped = true;
            }
            else if (_clockStopped)
            {
                _clockStopped = false;
                if (ctl != null) ctl.DoUpdateTime = true;
            }
        }

        private void OnTime(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !NetEnemies.ClientMode) return;
            int day = (int)r.R.ReadUInt32();
            int time = r.R.ReadUInt16();
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || !WorldShare.InWorld) return;
            var dreams = Singleton<Dreams>.Instance;
            if (dreams != null && (dreams.dreaming || dreams.switchingDream))
            {
                // The clock shows the dream's time; the world's waits in timeCopy, and catches up after waking.
                if (WorldActions.Debug && !_dreamLogged) Plugin.Log.LogInfo($"[world] dreaming: the clock waits (host at day {day} {time}, world time kept {dreams.timeCopy:0})");
                _dreamLogged = true;
                return;
            }
            _dreamLogged = false;

            // Minutes the client is behind, round the clock. (Darkwood counts a new day at dawn, in startDay, not at
            // midnight, so 1440 * day + minute isn't monotonic; the minute of the day, round the clock, is.)
            int behind = ((time - ctl.CurrentTime) % 1440 + 1440) % 1440;
            if (behind == 0 && ctl.day == day) return;
            if (behind <= MaxStepsPerUpdate && Mathf.Abs(day - ctl.day) <= 1)
            {
                for (int i = 0; i < behind; i++)
                {
                    ctl.CurrentTime++;
                    ctl.refreshTime(); // wraps at 1440; at dawn startDay() counts the new day
                }
                if (ctl.day != day) ctl.setDay(day);
                if (WorldActions.Debug && behind > 1) Plugin.Log.LogInfo($"[world] clock stepped {behind} minutes to day {ctl.day} {ctl.CurrentTime}");
                return;
            }
            // Far off (joining late, the host slept): go straight there.
            ctl.setDay(day);
            ctl.CurrentTime = time;
            ctl.refreshTime(afterGameLoad: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] clock jumped to day {ctl.day} {ctl.CurrentTime}");
        }
    }
}
