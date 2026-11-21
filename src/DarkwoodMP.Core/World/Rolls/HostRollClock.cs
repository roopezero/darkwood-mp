namespace DarkwoodMP.World.Rolls
{
    /// The host's timers for sending its rolls (unscaled time): a look
    /// every RollRules.CheckEvery, the scenario and weather when they
    /// change and every RollRules.Resend, the dawn's objects
    /// RollRules.ObjectsAfter after they were made. One per session.
    public sealed class HostRollClock
    {
        Seconds _nextCheck;
        Seconds _nextScenario;
        Seconds _nextWeather;
        Seconds _objectsAt;
        bool _objectsDue;
        string _sentScenario;
        string _sentWeather;

        /// The dawn's random objects were made: send them a moment later.
        /// A second call moves the time on.
        public void ObjectsMade(Seconds now)
        {
            _objectsDue = true;
            _objectsAt = new Seconds(now.Value + RollRules.ObjectsAfter);
        }

        /// Whether to send the dawn's objects now; true once per
        /// ObjectsMade.
        public bool ObjectsDue(Seconds now)
        {
            if (!_objectsDue || now.Value < _objectsAt.Value)
            {
                return false;
            }

            _objectsDue = false;
            return true;
        }

        /// Whether the host looks at its scenario and weather now; the
        /// next look is RollRules.CheckEvery later.
        public bool Checks(Seconds now)
        {
            if (now.Value < _nextCheck.Value)
            {
                return false;
            }

            _nextCheck = new Seconds(now.Value + RollRules.CheckEvery);
            return true;
        }

        /// Whether to send the night scenario now ("" for none). When it
        /// does, the scenario counts as sent.
        public RollSend Scenario(string scenario, Seconds now)
        {
            var send = Due(scenario != _sentScenario, _nextScenario, now);
            if (send != RollSend.None)
            {
                _sentScenario = scenario;
                _nextScenario = new Seconds(now.Value + RollRules.Resend);
            }

            return send;
        }

        /// Whether to send the weather now, by its key (WeatherKey.Of).
        /// When it does, the weather counts as sent.
        public RollSend Weather(string key, Seconds now)
        {
            var send = Due(key != _sentWeather, _nextWeather, now);
            if (send != RollSend.None)
            {
                _sentWeather = key;
                _nextWeather = new Seconds(now.Value + RollRules.Resend);
            }

            return send;
        }

        /// The session ended: nothing counts as sent, and the dawn's
        /// objects aren't due. The look and resend times stay.
        public void SessionEnded()
        {
            _sentScenario = null;
            _sentWeather = null;
            _objectsDue = false;
        }

        static RollSend Due(bool changed, Seconds next, Seconds at)
        {
            if (changed)
            {
                return RollSend.Changed;
            }

            if (at.Value >= next.Value)
            {
                return RollSend.Again;
            }

            return RollSend.None;
        }
    }
}
