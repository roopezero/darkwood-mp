using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    /// <summary>
    /// What the world rolls by itself, rolled once, on the host: the night's
    /// scenario and its events, the day's random events, the shadows' lights,
    /// the random objects that appear at dawn, night mushrooms, and the
    /// weather. A joiner's clock follows the host's a minute at a time
    /// (TimeSync), and each of these hangs on the clock, so without this every
    /// game rolled its own with its own random numbers.
    ///
    /// On a joiner in the shared world (not away in a separate location or a
    /// dream, which play as in single player) the rolls do nothing:
    /// NightScenarios.setCurrentScenario, NightScenario.checkFrequencies /
    /// fireEvent, the day events' Events.onUpdateTime, RandomEvent.fire,
    /// Player.shadowLightsTurnOff, WorldGenerator.spawnRandomObjects and
    /// despawnRandomObjects, WormsSpawner.spawn, and Rain.onNewDay /
    /// setUpNextRain / setUpFog. The host sends:
    ///   NightScenario  the scenario it chose (checked every second, sent when
    ///                  it changes and every 10 seconds).
    ///   NightEvent     a night event that fired. A joiner plays it at its own
    ///                  hideout, presentation only: of the event's GameEvent
    ///                  steps it runs sounds, particles, camera effects, colour
    ///                  tweens, animations and messages, and none of those that
    ///                  change the world (spawns, doors, items, flags...),
    ///                  whose results come from the host as they do. The
    ///                  shadows' lights come as their own NightEvent: which
    ///                  lamp stays on, so every game flickers the same lamps.
    ///   RandomObjects  the dawn's random objects (chunk, which object, where,
    ///                  what it holds), a moment after the host made them so
    ///                  their contents are filled; or one night mushroom.
    ///   Weather        the day's rain and fog plan, when it changes and every
    ///                  10 seconds.
    /// Enemies the events spawn on the host reach joiners through NetEnemies,
    /// and lamps switched on through ItemOn.
    /// </summary>
    internal sealed partial class WorldEvents
    {
        private readonly CoopSession _coop;
        private readonly HostRollClock _clock = new HostRollClock();

        /// <summary>
        /// Above 0 while a joiner plays a night event the host sent: only its
        /// presentation runs.
        /// </summary>
        public int Presentation { get; private set; }

        public WorldEvents(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.NightScenario, OnScenario);
            coop.Handle(MsgType.NightEvent, OnNightEvent);
            coop.Handle(MsgType.RandomObjects, OnRandomObjects);
            coop.Handle(MsgType.Weather, OnWeather);
        }

        private Session Net => _coop.Net;

        /// <summary>
        /// A joiner in the shared world: the host rolls the world's events,
        /// this game follows.
        /// </summary>
        public static bool Follows
        {
            get
            {
                var o = Singleton<OutsideLocations>.Instance;
                var d = Singleton<Dreams>.Instance;
                bool outside = o != null && o.playerInOutsideLocation;
                bool dreaming = d != null && d.dreaming;
                return RollRules.Follows(
                    NetEnemies.ClientMode, outside, dreaming);
            }
        }

        private bool HostSharing => Net.IsHost && _coop.Guards.Sharing;

        private static Seconds Now => new Seconds(Time.unscaledTime);

        private bool FromHost(ulong from)
        {
            return RollRules.FromHost(
                from, Net.HostId, WorldShare.InWorld);
        }

        public void Update()
        {
            if (!HostSharing)
            {
                return;
            }

            if (_clock.ObjectsDue(Now))
            {
                SendRandomObjects();
            }

            if (!_clock.Checks(Now))
            {
                return;
            }

            var ns = Singleton<NightScenarios>.Instance;
            string scenario = "";
            if (ns != null && ns.currentScenario != null)
            {
                scenario = ns.currentScenario.name;
            }

            RollSend send = _clock.Scenario(scenario, Now);
            if (send != RollSend.None)
            {
                if (WorldActions.Debug && send == RollSend.Changed)
                {
                    Plugin.Log.LogInfo(
                        $"[world] sent night scenario '{scenario}'");
                }

                var w = Net.Begin(MsgType.NightScenario);
                NightScenarioMessage.Write(w, scenario);
                Net.Broadcast(w, Channel.Control, reliable: true);
            }

            var rain = Singleton<Rain>.Instance;
            if (rain != null)
            {
                WeatherFacts facts = FactsOf(rain);
                string key = WeatherKey.Of(facts);
                send = _clock.Weather(key, Now);
                if (send != RollSend.None)
                {
                    if (WorldActions.Debug && send == RollSend.Changed)
                    {
                        Plugin.Log.LogInfo($"[world] sent weather {key}");
                    }

                    var w = Net.Begin(MsgType.Weather);
                    WeatherMessage.Write(w, facts);
                    Net.Broadcast(w, Channel.Control, reliable: true);
                }
            }
        }

        // ---- night scenario -------------------------------------------------

        private void OnScenario(ulong from, PacketReader r)
        {
            if (!FromHost(from))
            {
                return;
            }

            string name = NightScenarioMessage.Read(r);
            if (!Follows)
            {
                return;
            }

            var ns = Singleton<NightScenarios>.Instance;
            if (ns == null)
            {
                return;
            }

            NightScenario sc = null;
            if (name.Length > 0)
            {
                sc = ns.getScenario(name);
            }

            if (name.Length > 0 && sc == null)
            {
                Plugin.Log.LogWarning($"No night scenario {name} here");
                return;
            }

            if (ns.currentScenario == sc)
            {
                return;
            }

            ns.currentScenario = sc;
            if (sc != null)
            {
                ns.previousScenarioName = sc.name;
                ns.previousScenarioDifficulty = sc.difficulty;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] night scenario now '{name}' (the host's)");
            }
        }

        // ---- weather --------------------------------------------------------

        /// <summary>The day's rain and fog plan.</summary>
        private static WeatherFacts FactsOf(Rain r)
        {
            return new WeatherFacts
            {
                RainToday = r.rainToday,
                Start = r.timeToStart,
                Duration = r.duration,
                Lightning = r.preRainLightning,
                LightningStart = r.preRainLightningTime,
                LightningFor = r.lightningTime,
                FogIn = FogOf(r.timeToFadeInFog),
                FogOut = FogOf(r.timeToFadeOutFog),
            };
        }

        private static FogTime FogOf(TimeAndDay t)
        {
            if (t == null)
            {
                return new FogTime();
            }

            return new FogTime { Set = true, Day = t.day, Time = t.time };
        }

        private void OnWeather(ulong from, PacketReader r)
        {
            if (!FromHost(from))
            {
                return;
            }

            WeatherFacts sent = WeatherMessage.Read(r);
            var rain = Singleton<Rain>.Instance;
            if (!NetEnemies.ClientMode || rain == null)
            {
                return;
            }

            string before = WeatherKey.Of(FactsOf(rain));
            rain.rainToday = sent.RainToday;
            rain.preRainLightning = sent.Lightning;
            rain.timeToStart = sent.Start;
            rain.duration = sent.Duration;
            rain.preRainLightningTime = sent.LightningStart;
            rain.lightningTime = sent.LightningFor;
            if (sent.FogIn.Set)
            {
                rain.timeToFadeInFog =
                    new TimeAndDay(sent.FogIn.Time, sent.FogIn.Day);
            }

            if (sent.FogOut.Set)
            {
                rain.timeToFadeOutFog =
                    new TimeAndDay(sent.FogOut.Time, sent.FogOut.Day);
            }

            string now = WeatherKey.Of(FactsOf(rain));
            if (WorldActions.Debug && now != before)
            {
                Plugin.Log.LogInfo($"[world] weather now the host's: {now}");
            }
        }
    }
}
