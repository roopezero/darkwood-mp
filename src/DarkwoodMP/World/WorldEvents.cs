using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
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
    public sealed class WorldEvents
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

        private bool HostSharing => _session.IsHost && WorldActions.Sharing;

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

        // ---- night events ---------------------------------------------------

        private const byte EventFired = 1, ShadowLightsKind = 2;

        /// <summary>
        /// Host: a night event fired (CustomEvent.fire); the joiners play it
        /// too.
        /// </summary>
        internal void SendNightEvent(CustomEvent ev)
        {
            if (!HostSharing || WorldActions.Replaying || ev == null)
            {
                return;
            }

            var ns = Singleton<NightScenarios>.Instance;
            Location big = null;
            if (Player.Instance != null)
            {
                big = Player.Instance.whereAmI.bigLocation;
            }

            var w = _session.Begin(MsgType.NightEvent);
            w.W.Write(EventFired);
            string s = "";
            if (ns != null && ns.currentScenario != null)
            {
                s = ns.currentScenario.name;
            }

            w.WriteStr(s);
            w.WriteStr(ev.gameObject.name);
            string sentName = "";
            if (big != null)
            {
                sentName = big.name;
            }

            w.WriteStr(sentName);
            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                string shownName = "nowhere";
                if (big != null)
                {
                    shownName = big.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] sent night event {ev.gameObject.name} at "
                        + $"{shownName}");
            }
        }

        private void SendShadowLights(Generator gen, Item keep)
        {
            var w = _session.Begin(MsgType.NightEvent);
            w.W.Write(ShadowLightsKind);
            ObjRef.Of(gen.gameObject).Write(w);
            ObjRef.Of(keep.gameObject).Write(w);
            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent shadow lights: {keep.name} stays on "
                        + $"({gen.name})");
            }
        }

        internal static CustomEvent FindEvent(NightScenario sc, string name)
        {
            foreach (var e in sc.customEventAndInts)
            {
                if (e != null && e.customEvent != null
                    && e.customEvent.gameObject.name == name)
                {
                    return e.customEvent;
                }
            }

            foreach (var e in sc.customEvents)
            {
                if (e != null && e.gameObject.name == name)
                {
                    return e;
                }
            }

            return null;
        }

        private void OnNightEvent(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !WorldShare.InWorld)
            {
                return;
            }

            byte kind = r.R.ReadByte();
            if (kind == EventFired)
            {
                string scenario = r.ReadStr(),
                    name = r.ReadStr(),
                    where = r.ReadStr();
                if (!Follows)
                {
                    return;
                }

                var ns = Singleton<NightScenarios>.Instance;
                NightScenario sc = null;
                if (ns != null)
                {
                    sc = ns.getScenario(scenario);
                }

                CustomEvent ev = null;
                if (sc != null)
                {
                    ev = FindEvent(sc, name);
                }

                if (ev == null)
                {
                    Plugin.Log.LogWarning(
                        $"No night event {scenario}/{name} here");
                    return;
                }

                var big = Player.Instance.whereAmI.bigLocation;
                if (big == null)
                {
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] night event {name} (the host's, at "
                                + $"{where}): not at a location here, not "
                                + "played");
                    }

                    return;
                }

                ns.currentScenario = sc;
                Presentation++;
                WorldActions.Replaying = true;
                try
                {
                    ev.fire(force: true);
                }
                finally
                {
                    WorldActions.Replaying = false;
                    Presentation--;
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] night event {name} (the host's, at {where}) "
                            + $"played here at {big.name}");
                }
            }
            else if (kind == ShadowLightsKind)
            {
                var g = ObjRef.Read(r);
                var k = ObjRef.Read(r);
                if (!Follows)
                {
                    return;
                }

                var gen = ObjRef.Resolve<Generator>(g);
                var keep = ObjRef.Resolve<Item>(k);
                if (gen == null || keep == null)
                {
                    Plugin.Log.LogWarning(
                        $"Shadow lights: no generator {g} or lamp {k} here");
                    return;
                }

                WorldActions.Replaying = true;
                try
                {
                    ShadowLights(gen, keep);
                }
                finally
                {
                    WorldActions.Replaying = false;
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] shadow lights: {keep.name} stays on (the "
                            + "host's choice)");
                }
            }
        }

        /// <summary>
        /// Player.shadowLightsTurnOff on the host, with its lamp chosen once
        /// and sent: the chosen lamp comes on, all of the generator's lamps
        /// flicker, and after 8 seconds the others go out for 30.
        /// </summary>
        internal bool HostShadowLights(Player p, bool anyTime = false)
        {
            if (!anyTime
                && (Singleton<CharacterSpawner>.Instance.shadowsRemove
                    || Core.isDay()))
            {
                return false;
            }

            Generator gen = null;
            if (p.whereAmI.bigLocation != null)
            {
                gen = p.whereAmI.bigLocation.generator;
            }

            if (gen == null)
            {
                return false;
            }

            var choices = Lights(gen);
            choices.RemoveAll(l => l == null || l.dontTurnOnByShadowsEvent);
            if (choices.Count == 0)
            {
                return false;
            }

            var keep = choices[UnityEngine.Random.Range(0, choices.Count)];
            ShadowLights(gen, keep);
            SendShadowLights(gen, keep);
            return true;
        }

        private static List<Item> Lights(Generator gen)
        {
            var lights = new List<Item>(gen.powerItems);
            lights.Remove(gen.GetComponent<Item>());
            return lights;
        }

        /// <summary>
        /// The game's shadow lights effect, with the lamp that stays on given.
        /// </summary>
        internal static void ShadowLights(Generator gen, Item keep)
        {
            var lights = Lights(gen);
            if (lights.Count == 0)
            {
                return;
            }

            keep.turnOn();
            foreach (var l in lights)
            {
                if (l == null)
                {
                    continue;
                }

                l.enable();
                if (l.itemLight != null)
                {
                    l.itemLight.flicker_short(8f);
                }
            }

            lights.Remove(keep);
            Singleton<Controller>.Instance.Invoke(delegate
            {
                foreach (var l in lights)
                {
                    if (l != null)
                    {
                        l.empDisable(30f);
                    }
                }

                if (keep != null)
                {
                    keep.enable();
                }
            }, 8f, timeScaleDependent: true);
        }

        /// <summary>
        /// The GameEvent steps a joiner runs when it plays the host's night
        /// event: what's seen and heard.
        /// </summary>
        internal static bool PresentationOnly(GameEvent e)
        {
            switch (e.type)
            {
                case GameEvent.Type.sound:
                case GameEvent.Type.changeIdleSound:
                case GameEvent.Type.playSound_NIEUZYWAJ:
                case GameEvent.Type.particle:
                case GameEvent.Type.cameraEffect:
                case GameEvent.Type.tweenColor:
                case GameEvent.Type.playAnim:
                case GameEvent.Type.displayMessage:
                case GameEvent.Type.debug:
                    return true;
                case GameEvent.Type.fireEvent:
                    // other GameEvents (filtered the same way), not a world
                    // event
                    return !e.activeModifier;
                default:
                    return false;
            }
        }

        internal static IEnumerator Nothing()
        {
            yield break;
        }

        /// <summary>
        /// Runs a played event's step with Presentation and Replaying set, so
        /// what it fires later is filtered too.
        /// </summary>
        internal static IEnumerator AsPresentation(IEnumerator inner)
        {
            while (true)
            {
                // the first step runs at once, inside OnNightEvent's own replay
                bool more, was = WorldActions.Replaying;
                Presentation++;
                WorldActions.Replaying = true;
                try
                {
                    more = inner.MoveNext();
                }
                finally
                {
                    WorldActions.Replaying = was;
                    Presentation--;
                }

                if (!more)
                {
                    yield break;
                }

                yield return inner.Current;
            }
        }

        // ---- random objects at dawn, night mushrooms ------------------------

        private const byte DawnObjects = 1, NightObject = 2;
        private static readonly
            AccessTools.FieldRef<WorldChunk, List<GameObject>> RandomObjectsOf =
                AccessTools.FieldRefAccess<WorldChunk, List<GameObject>>(
                    "randomObjects");
        private readonly System.Random _rng = new System.Random();
        private long NewId() => ((long)_rng.Next() << 31) | (uint)_rng.Next();

        /// <summary>
        /// Host: the dawn's random objects were made; send them once their
        /// Start has filled what they hold.
        /// </summary>
        internal void RandomObjectsMade()
        {
            if (HostSharing)
            {
                _randomDue = Time.unscaledTime + 2f;
            }
        }

        private void SendRandomObjects()
        {
            var gen = Singleton<WorldGenerator>.Instance;
            var rwo = Singleton<RandomWorldObjects>.Instance;
            if (gen == null || rwo == null)
            {
                return;
            }

            var all = new List<(long id, int chunk, int obj, Vector3 local,
                string contents)>();
            for (int c = 0; c < gen.worldChunks.Count; c++)
            {
                var chunk = gen.worldChunks[c];
                if (chunk == null)
                {
                    continue;
                }

                foreach (var go in RandomObjectsOf(chunk))
                {
                    if (go == null)
                    {
                        continue;
                    }

                    int oi = rwo.objects.FindIndex(
                        o => o != null && o.prefab != null
                            && o.prefab.name == go.name);
                    if (oi < 0)
                    {
                        continue;
                    }

                    var r = ObjRef.Of(go);
                    long id;
                    if (r.K == ObjRef.Kind.Spawned)
                    {
                        id = r.Id;
                    }
                    else
                    {
                        id = NewId();
                    }

                    if (r.K != ObjRef.Kind.Spawned)
                    {
                        ObjRef.RegisterSpawned(id, go);
                    }

                    var inv = go.GetComponent<Inventory>();
                    Vector3 local = go.transform.localPosition;
                    string contents = "";
                    if (inv != null)
                    {
                        contents = JsonConvert.SerializeObject(
                            new Inventory.SaveState(inv));
                    }

                    all.Add((id, c, oi, local, contents));
                }
            }

            var w = _session.Begin(MsgType.RandomObjects);
            w.W.Write(DawnObjects);
            w.W.Write((ushort)all.Count);
            foreach (var o in all)
            {
                w.W.Write(o.id);
                w.W.Write((ushort)o.chunk);
                w.W.Write((ushort)o.obj);
                w.Write(o.local);
                w.WriteStr(o.contents);
            }

            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {all.Count} random objects of the dawn");
            }
        }

        /// <summary>
        /// Host: a night mushroom was made (WormsSpawner.spawn).
        /// </summary>
        internal void SendNightObject(GameObject go, string prefab)
        {
            if (!HostSharing || go == null)
            {
                return;
            }

            long id = NewId();
            ObjRef.RegisterSpawned(id, go);
            var w = _session.Begin(MsgType.RandomObjects);
            w.W.Write(NightObject);
            w.W.Write(id);
            w.WriteStr(prefab);
            w.Write(go.transform.position);
            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent night object {prefab} #{id} at "
                        + $"{go.transform.position}");
            }
        }

        private void OnRandomObjects(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !WorldShare.InWorld)
            {
                return;
            }

            byte kind = r.R.ReadByte();
            if (kind == DawnObjects)
            {
                int n = r.R.ReadUInt16();
                var list = new List<(long id, int chunk, int obj,
                    Vector3 local, string contents)>(n);
                for (int i = 0; i < n; i++)
                {
                    list.Add(
                        (r.R.ReadInt64(), r.R.ReadUInt16(), r.R.ReadUInt16(), r
                            .ReadVector3(), r.ReadStr()));
                }

                if (!NetEnemies.ClientMode)
                {
                    return; // away or not: the world's objects are the host's
                }

                var gen = Singleton<WorldGenerator>.Instance;
                var rwo = Singleton<RandomWorldObjects>.Instance;
                if (gen == null || rwo == null)
                {
                    return;
                }

                foreach (var chunk in gen.worldChunks)
                {
                    if (chunk == null)
                    {
                        continue;
                    }

                    var objs = RandomObjectsOf(chunk);
                    foreach (var go in objs)
                    {
                        if (go != null)
                        {
                            Object.Destroy(go);
                        }
                    }

                    objs.Clear();
                }

                int made = 0;
                WorldActions.Replaying = true;
                try
                {
                    foreach (var o in list)
                    {
                        if (o.chunk >= gen.worldChunks.Count
                            || o.obj >= rwo.objects.Count)
                        {
                            continue;
                        }

                        var chunk = gen.worldChunks[o.chunk];
                        RandomWorldObjects.RandomObject randomObject =
                            rwo.objects[o.obj];
                        GameObject prefab = null;
                        if (randomObject != null)
                        {
                            prefab = randomObject.prefab;
                        }

                        if (chunk == null || prefab == null)
                        {
                            continue;
                        }

                        // as WorldChunk.tryToSpawnInChunk
                        var go = Core.AddPrefab(
                            prefab, o.local, Quaternion.Euler(90f, 0f, 0f),
                            chunk.gameObject);
                        if (go == null)
                        {
                            continue;
                        }

                        Core.addToSaveable(go, isDynamic: true);
                        WorldGrid grid = Singleton<WorldGrid>.Instance;
                        if (grid != null)
                        {
                            grid.registerToNode(go);
                        }

                        RandomObjectsOf(chunk).Add(go);
                        ObjRef.RegisterSpawned(o.id, go);
                        var inv = go.GetComponent<Inventory>();
                        if (inv != null && o.contents.Length > 0)
                        {
                            Containers instance = Containers.Instance;
                            if (instance != null)
                            {
                                instance.Apply(inv, o.contents);
                            }
                        }

                        made++;
                    }
                }
                finally
                {
                    WorldActions.Replaying = false;
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] made the host's {made} of {n} random objects "
                            + "of the dawn");
                }
            }
            else if (kind == NightObject)
            {
                long id = r.R.ReadInt64();
                string name = r.ReadStr();
                var pos = r.ReadVector3();
                if (!NetEnemies.ClientMode || ObjRef.Spawned.ContainsKey(id))
                {
                    return;
                }

                SaveManager saves = Singleton<SaveManager>.Instance;
                GameObject prefab = null;
                if (saves != null)
                {
                    prefab = saves.getPrefab(name);
                }

                if (prefab == null)
                {
                    Plugin.Log.LogWarning($"No night object {name} here");
                    return;
                }

                GameObject go;
                WorldActions.Replaying = true;
                try
                {
                    go = Core.AddPrefab(
                        prefab, pos, Quaternion.Euler(90f, 0f, 0f), null,
                        worldSpace: true);
                }
                finally
                {
                    WorldActions.Replaying = false;
                }

                if (go == null)
                {
                    return;
                }

                var chunk = WorldChunk.getChunkAtPos(pos);
                if (chunk != null)
                {
                    // gone at dawn, as the host's
                    chunk.nightSpawnedObjects.Add(go);
                }

                ObjRef.RegisterSpawned(id, go);
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] made the host's night object {name} #{id} at "
                            + $"{pos}");
                }
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

    // ---- joiners don't roll -------------------------------------------------

    [HarmonyPatch(
        typeof(NightScenarios), nameof(NightScenarios.setCurrentScenario))]
    static class ScenarioChoicePatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    [HarmonyPatch(
        typeof(NightScenario), nameof(NightScenario.checkFrequencies))]
    static class NightEventRollPatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    [HarmonyPatch(typeof(NightScenario), nameof(NightScenario.fireEvent))]
    static class NightEventPickPatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    /// <summary>
    /// The day's random events (a redneck ambush, world events) at their random
    /// times.
    /// </summary>
    [HarmonyPatch(typeof(Events), "onUpdateTime")]
    static class DayEventsPatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    /// <summary>
    /// A joiner's own random events don't fire; a night event the host sent
    /// plays only its location events' presentation.
    /// </summary>
    [HarmonyPatch(
        typeof(RandomEvent), nameof(RandomEvent.fire),
        new[] { typeof(bool), typeof(bool) })]
    static class RandomEventPatch
    {
        static bool Prefix(RandomEvent __instance) =>
            !WorldEvents.Follows
                || (WorldEvents.Presentation > 0
                    && __instance.type == RandomEvent.Type.locationEvent);
    }

    /// <summary>Host: a night event fired; send it.</summary>
    [HarmonyPatch(typeof(CustomEvent), nameof(CustomEvent.fire))]
    static class NightEventSendPatch
    {
        static void Postfix(CustomEvent __instance)
        {
            WorldEvents instance = WorldEvents.Instance;
            if (instance != null)
            {
                instance.SendNightEvent(__instance);
            }
        }
    }

    /// <summary>
    /// A joiner playing the host's night event runs only the steps that are
    /// seen or heard.
    /// </summary>
    [HarmonyPatch(
        typeof(GameEvent), nameof(GameEvent.fire),
        new[] { typeof(GameObject) })]
    static class PresentationPatch
    {
        static void Postfix(GameEvent __instance, ref IEnumerator __result)
        {
            if (WorldEvents.Presentation <= 0 || !WorldEvents.Follows)
            {
                return;
            }

            if (WorldEvents.PresentationOnly(__instance))
            {
                __result = WorldEvents.AsPresentation(__result);
            }
            else
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] night event step {__instance.type} "
                            + $"({__instance.Value}) left to the host");
                }

                __result = WorldEvents.Nothing();
            }
        }
    }

    /// <summary>
    /// The shadows' lights: the host picks the lamp and sends it; joiners wait
    /// for it.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.shadowLightsTurnOff))]
    static class ShadowLightsPatch
    {
        static bool Prefix(Player __instance)
        {
            if (WorldEvents.Follows)
            {
                return false;
            }

            if (Session.Instance != null && Session.Instance.IsHost
                && WorldActions.Sharing && WorldEvents.Instance != null)
            {
                WorldEvents.Instance.HostShadowLights(__instance);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.spawnRandomObjects))]
    static class RandomObjectsPatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
        static void Postfix()
        {
            if (!NetEnemies.ClientMode)
            {
                WorldEvents instance = WorldEvents.Instance;
                if (instance != null)
                {
                    instance.RandomObjectsMade();
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.despawnRandomObjects))]
    static class RandomObjectsGonePatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
    }

    /// <summary>Night mushrooms: made on the host and sent.</summary>
    [HarmonyPatch(typeof(WormsSpawner), nameof(WormsSpawner.spawn))]
    static class NightObjectPatch
    {
        static bool Prefix(out (WorldChunk chunk, int count) __state)
        {
            __state = (null, 0);
            if (WorldEvents.Follows)
            {
                return false;
            }

            var p = Player.Instance;
            // The game lists the new mushroom in the chunk the player is in.
            WorldChunk chunk = null;
            if (p != null)
            {
                chunk = WorldChunk.getChunkAtPos(p._transform.position);
            }

            if (chunk != null)
            {
                __state = (chunk, chunk.nightSpawnedObjects.Count);
            }

            return true;
        }

        static void Postfix((WorldChunk chunk, int count) __state)
        {
            if (__state.chunk == null)
            {
                return;
            }

            var list = __state.chunk.nightSpawnedObjects;
            var big = Player.Instance.whereAmI.bigLocation;
            if (list.Count > __state.count && big != null
                && big.nightMushroom != null)
            {
                WorldEvents instance = WorldEvents.Instance;
                if (instance != null)
                {
                    instance.SendNightObject(
                        list[list.Count - 1], big.nightMushroom.name);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.onNewDay))]
    static class RainDayPatch
    {
        /// <summary>
        /// A joiner keeps the new day's fog reset (not random); the rest is the
        /// host's, in Weather.
        /// </summary>
        static bool Prefix(Rain __instance)
        {
            if (!NetEnemies.ClientMode)
            {
                return true;
            }

            __instance.fogFadedInToday = false;
            __instance.fogFadedOutToday = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.setUpNextRain))]
    static class RainNextPatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.setUpFog))]
    static class FogPatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
    }

    /// <summary>
    /// Host: the day's redneck ambush (RandomEvent spawnRedneck) can be any
    /// player's. The game sends one redneck at the host's player when it's
    /// outdoors, away from any location, from day 2. With others playing, the
    /// ambush picks at random among every player that holds for (the host's
    /// player, and each living joiner's proxy that is outdoors and away from
    /// any location); for a joiner the redneck is spawned around their proxy
    /// and goes for them, as the game's does for the host. It's the host's
    /// enemy like any other, so it reaches the joiners as an EnemySpawn.
    /// </summary>
    [HarmonyPatch(
        typeof(RandomEvent), nameof(RandomEvent.fire),
        new[] { typeof(bool), typeof(bool) })]
    static class RedneckAmbushPatch
    {
        private static readonly System.Reflection.MethodInfo RemoveMe =
            AccessTools.Method(typeof(RandomEvent), "removeMe");

        static bool Prefix(
            RandomEvent __instance, bool checkIfRequirementsMet, bool force)
        {
            var e = __instance;
            if (NetEnemies.ClientMode || !Threat.Enabled
                || e.type != RandomEvent.Type.spawnRedneck)
            {
                return true;
            }

            // The game's own gate (it returns before doing anything): left to
            // it.
            bool wants = (!e.disabled || force)
                && ((((checkIfRequirementsMet && e.requirementsMet())
                            || !checkIfRequirementsMet)
                        && (!e.startedToday || e.multipleTimesPerDay))
                    || force);
            if (!wants)
            {
                return true;
            }

            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || ctl.day < 2)
            {
                return true;
            }

            var host = Player.Instance;
            bool hostCan = host != null && host.alive && !host.isInside
                && host.whereAmI != null && host.whereAmI.bigLocation == null;
            var joiners = new List<NetPlayerProxy>();
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p != null && p.alive
                    && !JoinerAi.InsideAt(p.transform.position)
                    && NightSpawns.BigLocationAt(p.transform.position) == null)
                {
                    joiners.Add(p);
                }
            }

            if (joiners.Count == 0)
            {
                // only the host's player could be ambushed: the game's own
                return true;
            }

            int hostSlot = 0;
            if (hostCan)
            {
                hostSlot = 1;
            }

            int pick = UnityEngine.Random.Range(0, joiners.Count + hostSlot);
            if (pick >= joiners.Count)
            {
                return true; // the host's player: the game's own
            }

            var proxy = joiners[pick];
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                proxy.gameObject, Vector3.zero, 1500f, "Redneck02",
                nocturnal: false, attackPlayer: false, relentlessPursuit: true);
            if (c != null)
            {
                if (c.aggressiveness != Aggressiveness.defensive)
                {
                    c.aggressiveness = Aggressiveness.attackOnSight;
                }

                c.attackCharacter(proxy.transform);
                c.attackType = AttackType.hitAndRun;
                c.wantToDespawn = true;
                c.maxStamina = 1000f;
                float distance = Core.trueDistance(
                    c.transform.position, proxy.transform.position);
                Plugin.Log.LogInfo(
                    $"[events] the day's redneck ambush is {proxy.OwnerName}'s "
                        + $"({distance:0} from them)");
            }

            e.randomizeStartTime();
            if (c != null)
            {
                e.startedToday = true;
                if (e.removeOnFire)
                {
                    System.Reflection.MethodInfo removeMe = RemoveMe;
                    if (removeMe != null)
                    {
                        removeMe.Invoke(e, null);
                    }
                }
            }

            return false;
        }
    }
}
