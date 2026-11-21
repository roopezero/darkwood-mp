using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps that log what the enemies near the players do and why
    /// ([dogwatch] lines), to find what wakes the save's sleeping and eating
    /// dogs.
    ///   dw-on / dw-off   start / stop the watch in this game. Host: each enemy
    ///                    near a player, when its sleeping, waking, eating,
    ///                    behaviour, target or life changes, with how far it is
    ///                    from each player; what woke or alerted it
    ///                    (Character.wakeup / beAlerted / the sounds it heard,
    ///                    with their source). Client: where our player is and
    ///                    how close our copies of the host's enemies are.
    ///   dw-list          the enemies within 3000 of the centre, with their
    ///                    state.
    /// </summary>
    public static class MapScenarioDogWatch
    {
        public static bool On;
        private static GameObject _go;

        public static void Register()
        {
            MapScenario.Add("dw-on", (a, r) =>
            {
                Start();
                r.Info = "watching";
                return None();
            });

            MapScenario.Add("dw-off", (a, r) =>
            {
                r.Info = Watch.Summary();
                Stop();
                return None();
            });

            MapScenario.Add("dw-list", (a, r) =>
            {
                r.Info = List();
                return None();
            });
        }

        private static void Start()
        {
            On = true;
            Watch.Reset();
            if (_go == null)
            {
                _go = new GameObject("DarkwoodMP.DogWatch");
                Object.DontDestroyOnLoad(_go);
                _go.AddComponent<Watch>();
            }
        }

        private static void Stop()
        {
            On = false;
            if (_go != null)
            {
                Object.Destroy(_go);
            }

            _go = null;
        }

        private static string List()
        {
            var c0 = MapScenario.Centre;
            var parts = new List<string>();
            foreach (var c in Object.FindObjectsOfType<Character>())
            {
                if (c == null || c.npc != null || !c.alive)
                {
                    continue;
                }

                var p = c.transform.position;
                if (StateDump.Flat(p - c0) > 3000f)
                {
                    continue;
                }

                string text = "";
                if (c.sleeping)
                {
                    text = " asleep";
                }

                string eatingNote = "";
                if (c.eating)
                {
                    eatingNote = " eating";
                }

                string aniNote = "";
                if (c.playingCustomAni)
                {
                    aniNote = " ani:" + c.customAni;
                }

                parts.Add(
                    $"{c.name}@{p.x - c0.x:0},{p.z - c0.z:0}{text}{eatingNote}{aniNote}");
            }

            Plugin.Log.LogInfo("[dogwatch] list: " + string.Join("; ", parts));
            return parts.Count + " enemies: " + string.Join(
                "; ", parts.Take(30));
        }

        internal static string Id(Character c)
        {
            if (NetEnemies.Instance != null
                && NetEnemies.Instance.NetIdOf(c, out uint id))
            {
                return id.ToString();
            }

            return "i" + c.GetInstanceID();
        }

        internal static string Rel(Vector3 p)
        {
            var c = MapScenario.Centre;
            return $"{p.x - c.x:0},{p.z - c.z:0}";
        }

        /// <summary>
        /// How far from each player (host's own and the proxies), as "host 120
        /// / client 45 (proxy at x,z)".
        /// </summary>
        internal static string Dists(Vector3 p)
        {
            var parts = new List<string>();
            if (Player.Instance != null)
            {
                parts.Add(
                    "me "
                        + $"{StateDump.Flat(Player.Instance.transform.position - p):0}");
            }

            foreach (var px in NetPlayerProxy.All.Values)
            {
                if (px != null)
                {
                    parts.Add(
                        $"{px.OwnerName} "
                            + $"{StateDump.Flat(px.transform.position - p):0} "
                            + $"(at {Rel(px.transform.position)})");
                }
            }

            return string.Join(" / ", parts);
        }

        internal static string Caller()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            var names = new List<string>();
            for (int i = 0; i < st.FrameCount && names.Count < 5; i++)
            {
                System.Diagnostics.StackFrame frame = st.GetFrame(i);
                System.Reflection.MethodBase m = null;
                if (frame != null)
                {
                    m = frame.GetMethod();
                }

                if (m == null)
                {
                    continue;
                }

                string t = "?";
                if (m.DeclaringType != null)
                {
                    t = m.DeclaringType.Name;
                }

                if (t.Contains("Patch") || t == "MapScenarioDogWatch")
                {
                    continue;
                }

                names.Add(t + "." + m.Name);
            }

            return string.Join(" < ", names);
        }

        internal static bool Watched(Character c)
        {
            if (!On || c == null || c.npc != null)
            {
                return false;
            }

            var s = Net.Session.Instance;
            if (s == null || !s.IsHost)
            {
                return false;
            }

            return StateDump.Flat(c.transform.position - MapScenario.Centre)
                < 3000f;
        }

        private sealed class Watch : MonoBehaviour
        {
            private struct St
            {
                public bool Sleep, Waking, Eat, Alive, Ani, Atk;
                public Character.Behaviour B;
                public Transform T;
            }

            private static readonly Dictionary<Character, St> _last =
                new Dictionary<Character, St>();
            private static float _next, _nextScan, _nextPos;
            private static List<Character> _list = new List<Character>();
            public static int Wakes, Alerts, Changes, Close;
            private static readonly HashSet<uint> _closeNow =
                new HashSet<uint>();

            public static void Reset()
            {
                _last.Clear();
                _list.Clear();
                Wakes = Alerts = Changes = Close = 0;
                _closeNow.Clear();
                _next = _nextScan = _nextPos = 0f;
            }

            public static void Woke() => Wakes++;
            public static void Alerted() => Alerts++;
            public static string Summary() =>
                $"wakes {Wakes}, alerts {Alerts}, state changes {Changes}, "
                    + $"close contacts {Close}";

            private void Update()
            {
                if (!On || Time.unscaledTime < _next)
                {
                    return;
                }

                _next = Time.unscaledTime + 0.1f;
                var s = Net.Session.Instance;
                if (s == null)
                {
                    return;
                }

                if (s.IsHost)
                {
                    HostTick();
                }
                else
                {
                    ClientTick();
                }
            }

            private static void HostTick()
            {
                if (Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + 3f;
                    _list = Object.FindObjectsOfType<Character>()
                        .Where(
                            c => c != null && c.npc == null
                                && StateDump.Flat(
                                    c.transform.position - MapScenario.Centre)
                                    < 3000f).ToList();
                }

                foreach (var c in _list)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    var st = new St
                    {
                        Sleep = c.sleeping,
                        Waking = c.wakingUp,
                        Eat = c.eating,
                        Alive = c.alive,
                        Ani = c.playingCustomAni,
                        Atk = c.attacking,
                        B = c.behaviour,
                        T = c.target
                    };

                    if (_last.TryGetValue(c, out var o) && o.Sleep == st.Sleep
                        && o.Waking == st.Waking && o.Eat == st.Eat
                        && o.Alive == st.Alive && o.Ani == st.Ani
                        && o.Atk == st.Atk && (o.B == st.B || o.Eat && st.Eat)
                        && o.T == st.T)
                    {
                        continue;
                    }

                    bool first = !_last.ContainsKey(c);
                    _last[c] = st;
                    if (first)
                    {
                        continue;
                    }

                    Changes++;
                    string name = c.name;
                    string label = Id(c);
                    string place = Rel(c.transform.position);
                    string dead = "dead ";
                    if (st.Alive)
                    {
                        dead = "";
                    }

                    string asleep = "";
                    if (st.Sleep)
                    {
                        asleep = "asleep ";
                    }

                    string waking = "";
                    if (st.Waking)
                    {
                        waking = "waking ";
                    }

                    string eating = "";
                    if (st.Eat)
                    {
                        eating = "eating ";
                    }

                    string ani = "";
                    if (st.Ani)
                    {
                        ani = "ani:" + c.customAni + " ";
                    }

                    string attacking = "";
                    if (st.Atk)
                    {
                        string attack = "?";
                        if (c.currentAttack != null)
                        {
                            attack = c.currentAttack.name;
                        }

                        attacking = "attacking:" + attack + " ";
                    }

                    string target = "-";
                    if (st.T != null)
                    {
                        target = st.T.name;
                    }

                    Plugin.Log.LogInfo(
                        $"[dogwatch] {name}#{label} at {place}: "
                            + $"{dead}{asleep}{waking}{eating}{ani}{attacking}{st.B} -> {target}; {Dists(c.transform.position)}");
                }

                if (Time.unscaledTime >= _nextPos)
                {
                    _nextPos = Time.unscaledTime + 1f;
                    foreach (var px in NetPlayerProxy.All.Values)
                    {
                        if (px != null)
                        {
                            float now = Time.unscaledTime;
                            string owner = px.OwnerName;
                            string proxyPlace = Rel(px.transform.position);
                            string myPlace = "-";
                            if (Player.Instance != null)
                            {
                                myPlace =
                                    Rel(Player.Instance.transform.position);
                            }

                            Plugin.Log.LogInfo(
                                $"[dogwatch] t {now:0.0}: proxy {owner} at "
                                    + $"{proxyPlace}, me at {myPlace}");
                        }
                    }
                }
            }

            private static void ClientTick()
            {
                var ne = NetEnemies.Instance;
                var me = Player.Instance;
                if (ne == null || me == null)
                {
                    return;
                }

                var at = me.transform.position;
                bool log = Time.unscaledTime >= _nextPos;
                if (log)
                {
                    _nextPos = Time.unscaledTime + 1f;
                }

                var near = new List<string>();
                foreach (var c in ne.PuppetCharacters())
                {
                    float d = StateDump.Flat(c.transform.position - at);
                    ne.NetIdOf(c, out uint id);
                    if (d < 45f)
                    {
                        if (_closeNow.Add(id))
                        {
                            Close++;
                            string name = c.name;
                            string place = Rel(c.transform.position);
                            string asleep = "";
                            if (c.sleeping)
                            {
                                asleep = " asleep";
                            }

                            Plugin.Log.LogInfo(
                                "[dogwatch] our player touches the host's "
                                    + $"{name} #{id}: {d:0} apart, it at "
                                    + $"{place}{asleep}, we at {Rel(at)}");
                        }
                    }
                    else
                    {
                        _closeNow.Remove(id);
                    }

                    if (d < 400f)
                    {
                        string text = "";
                        if (c.sleeping)
                        {
                            text = " asleep";
                        }

                        near.Add($"{c.name}#{id} {d:0}{text}");
                    }
                }

                if (log && near.Count > 0)
                {
                    Plugin.Log.LogInfo(
                        $"[dogwatch] t {Time.unscaledTime:0.0}: we at "
                            + $"{Rel(at)}; near: {string.Join(", ", near)}");
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.wakeup))]
        static class WakePatch
        {
            static void Prefix(Character __instance)
            {
                if (!Watched(__instance) || !__instance.sleeping
                    || __instance.wakingUp)
                {
                    return;
                }

                Watch.Woke();
                string name = __instance.name;
                string label = Id(__instance);
                string place = Rel(__instance.transform.position);
                string caller = Caller();
                string source = "-";
                if (Threat.NoiseSource != null)
                {
                    source = Threat.NoiseSource.name;
                }

                Plugin.Log.LogInfo(
                    $"[dogwatch] WAKE {name}#{label} at {place} by {caller}; "
                        + $"noise source {source}; "
                        + $"{Dists(__instance.transform.position)}");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.beAlerted))]
        static class AlertPatch
        {
            static void Prefix(
                Character __instance, Transform destTarget,
                bool isAggressiveAction)
            {
                if (!Watched(__instance) || __instance.target == destTarget)
                {
                    return;
                }

                Watch.Alerted();
                string name = __instance.name;
                string label = Id(__instance);
                string place = Rel(__instance.transform.position);
                string asleep = "";
                if (__instance.sleeping)
                {
                    asleep = " asleep";
                }

                string eating = "";
                if (__instance.eating)
                {
                    eating = " eating";
                }

                string target = "-";
                if (destTarget != null)
                {
                    target = destTarget.name;
                }

                Plugin.Log.LogInfo(
                    $"[dogwatch] ALERT {name}#{label} at "
                        + $"{place}{asleep}{eating} at {target} aggressive "
                        + $"{isAggressiveAction} by {Caller()}; "
                        + $"{Dists(__instance.transform.position)}");
            }
        }

        private static readonly Dictionary<int, float> _heardAt =
            new Dictionary<int, float>();

        [HarmonyPatch(typeof(Character), nameof(Character.heardSound), new[]
        {
            typeof(Vector3),
            typeof(float),
            typeof(bool),
            typeof(float),
            typeof(bool)
        })]
        static class HearPatch
        {
            static void Prefix(Character __instance, out float __state)
            {
                if (__instance != null)
                {
                    __state = __instance.hearingCounter;
                }
                else
                {
                    __state = 0f;
                }
            }

            static void Postfix(
                Character __instance, Vector3 Position, float soundRange,
                bool dangerousSound, float volume, float __state)
            {
                if (!Watched(__instance))
                {
                    return;
                }

                bool crossed = __instance.hearingCounter >= 1f && __state < 1f
                    || dangerousSound;
                int id = __instance.GetInstanceID();
                if (!crossed && _heardAt.TryGetValue(id, out var t)
                    && Time.unscaledTime - t < 2f)
                {
                    return;
                }

                _heardAt[id] = Time.unscaledTime;
                string name = __instance.name;
                string label = Id(__instance);
                string asleep = "";
                if (__instance.sleeping)
                {
                    asleep = " asleep";
                }

                string eating = "";
                if (__instance.eating)
                {
                    eating = " eating";
                }

                string loud = "";
                if (crossed)
                {
                    loud = "LOUD ";
                }

                float counter = __instance.hearingCounter;
                string origin = Rel(Position);
                float distance =
                    StateDump.Flat(Position - __instance.transform.position);
                string source = "-";
                if (Threat.NoiseSource != null)
                {
                    source = Threat.NoiseSource.name;
                }

                Plugin.Log.LogInfo(
                    $"[dogwatch] HEARD {name}#{label}{asleep}{eating} "
                        + $"{loud}counter {__state:0.00}->{counter:0.00} from "
                        + $"{origin} ({distance:0} away) range {soundRange:0} "
                        + $"vol {volume:0.00} danger {dangerousSound} source "
                        + $"{source} by {Caller()}");
            }
        }
    }
}
