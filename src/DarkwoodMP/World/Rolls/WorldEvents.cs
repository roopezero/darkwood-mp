using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
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
    public sealed partial class WorldEvents
    {
        public static WorldEvents Instance { get; private set; }
        private readonly Session _session;
        private const float Resend = 10f;
        private float _nextCheck,
            _nextScenarioSend,
            _nextWeatherSend,
            _randomDue = -1f;
        private string _sentScenario;
        private string _sentWeather;

        /// <summary>
        /// Above 0 while a joiner plays a night event the host sent: only its
        /// presentation runs.
        /// </summary>
        public static int Presentation;

        public WorldEvents(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.NightScenario, OnScenario);
            session.Register(MsgType.NightEvent, OnNightEvent);
            session.Register(MsgType.RandomObjects, OnRandomObjects);
            session.Register(MsgType.Weather, OnWeather);
            session.OnSessionEnded += () =>
            {
                _sentScenario = null;
                _sentWeather = null;
                _randomDue = -1f;
                Presentation = 0;
            };
        }

        private static bool Away()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            return (o != null && o.playerInOutsideLocation)
                || (d != null && d.dreaming);
        }

        /// <summary>
        /// A joiner in the shared world: the host rolls the world's events,
        /// this game follows.
        /// </summary>
        public static bool Follows => NetEnemies.ClientMode && !Away();

        private bool HostSharing => _session.IsHost && Coop.Guards.Sharing;

        public void Update()
        {
            if (!HostSharing)
            {
                return;
            }

            if (_randomDue >= 0f && Time.unscaledTime >= _randomDue)
            {
                _randomDue = -1f;
                SendRandomObjects();
            }

            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.unscaledTime + 1f;

            var ns = Singleton<NightScenarios>.Instance;
            string scenario = "";
            if (ns != null && ns.currentScenario != null)
            {
                scenario = ns.currentScenario.name;
            }

            if (scenario != _sentScenario
                || Time.unscaledTime >= _nextScenarioSend)
            {
                if (WorldActions.Debug && scenario != _sentScenario)
                {
                    Plugin.Log.LogInfo(
                        $"[world] sent night scenario '{scenario}'");
                }

                _sentScenario = scenario;
                _nextScenarioSend = Time.unscaledTime + Resend;
                var w = _session.Begin(MsgType.NightScenario);
                w.WriteStr(scenario);
                _session.Broadcast(w, Channel.Control, reliable: true);
            }

            var rain = Singleton<Rain>.Instance;
            if (rain != null)
            {
                string key = WeatherKey(rain);
                if (key != _sentWeather
                    || Time.unscaledTime >= _nextWeatherSend)
                {
                    if (WorldActions.Debug && key != _sentWeather)
                    {
                        Plugin.Log.LogInfo($"[world] sent weather {key}");
                    }

                    _sentWeather = key;
                    _nextWeatherSend = Time.unscaledTime + Resend;
                    SendWeather(rain);
                }
            }
        }

        // ---- night scenario -------------------------------------------------

        private void OnScenario(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !WorldShare.InWorld)
            {
                return;
            }

            string name = r.ReadStr();
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

        private static string WeatherKey(Rain r)
        {
            int rain = 0;
            if (r.rainToday)
            {
                rain = 1;
            }

            int lightning = 0;
            if (r.preRainLightning)
            {
                lightning = 1;
            }

            return $"rain {rain} at {r.timeToStart:0} for {r.duration:0}, "
                + $"lightning {lightning} {r.preRainLightningTime:0} "
                + $"{r.lightningTime:0}, " +
                $"fog {Tad(r.timeToFadeInFog)}-{Tad(r.timeToFadeOutFog)}";
        }

        private static string Tad(TimeAndDay t)
        {
            if (t == null)
            {
                return "none";
            }

            return $"{t.day}/{t.time}";
        }

        private void SendWeather(Rain r)
        {
            var w = _session.Begin(MsgType.Weather);
            int flags = 0;
            if (r.rainToday)
            {
                flags |= 1;
            }

            if (r.preRainLightning)
            {
                flags |= 2;
            }

            w.W.Write((byte)flags);
            w.W.Write(r.timeToStart);
            w.W.Write(r.duration);
            w.W.Write(r.preRainLightningTime);
            w.W.Write(r.lightningTime);
            WriteTad(w, r.timeToFadeInFog);
            WriteTad(w, r.timeToFadeOutFog);
            _session.Broadcast(w, Channel.Control, reliable: true);
        }

        private static void WriteTad(PacketWriter w, TimeAndDay t)
        {
            int time = -1;
            if (t != null)
            {
                time = t.time;
            }

            w.W.Write(time);
            int day = -1;
            if (t != null)
            {
                day = t.day;
            }

            w.W.Write(day);
        }

        private static TimeAndDay ReadTad(PacketReader r)
        {
            int time = r.R.ReadInt32(), day = r.R.ReadInt32();
            if (time < 0)
            {
                return null;
            }

            return new TimeAndDay(time, day);
        }

        private void OnWeather(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !WorldShare.InWorld)
            {
                return;
            }

            byte f = r.R.ReadByte();
            float start = r.R.ReadSingle(),
                duration = r.R.ReadSingle(),
                pre = r.R.ReadSingle(),
                lightning = r.R.ReadSingle();
            var fogIn = ReadTad(r);
            var fogOut = ReadTad(r);
            var rain = Singleton<Rain>.Instance;
            if (!NetEnemies.ClientMode || rain == null)
            {
                return;
            }

            string before = WeatherKey(rain);
            rain.rainToday = (f & 1) != 0;
            rain.preRainLightning = (f & 2) != 0;
            rain.timeToStart = start;
            rain.duration = duration;
            rain.preRainLightningTime = pre;
            rain.lightningTime = lightning;
            if (fogIn != null)
            {
                rain.timeToFadeInFog = fogIn;
            }

            if (fogOut != null)
            {
                rain.timeToFadeOutFog = fogOut;
            }

            string now = WeatherKey(rain);
            if (WorldActions.Debug && now != before)
            {
                Plugin.Log.LogInfo($"[world] weather now the host's: {now}");
            }
        }
    }
}
