using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps that log what the enemies near the players do and why ([dogwatch] lines), to find what wakes
    /// the save's sleeping and eating dogs.
    ///   dw-on / dw-off   start / stop the watch in this game. Host: each enemy near a player, when its sleeping,
    ///                    waking, eating, behaviour, target or life changes, with how far it is from each player;
    ///                    what woke or alerted it (Character.wakeup / beAlerted / the sounds it heard, with their
    ///                    source). Client: where our player is and how close our copies of the host's enemies are.
    ///   dw-list          the enemies within 3000 of the centre, with their state.
    /// </summary>
    public static class MapScenarioDogWatch
    {
        public static bool On;
        private static GameObject _go;

        public static void Register()
        {
            MapScenario.Add("dw-on", (a, r) => { Start(); r.Info = "watching"; return None(); });
            MapScenario.Add("dw-off", (a, r) => { r.Info = Watch.Summary(); Stop(); return None(); });
            MapScenario.Add("dw-list", (a, r) => { r.Info = List(); return None(); });
        }

        private static IEnumerator None() { yield break; }

        private static void Start()
        {
            On = true;
            Watch.Reset();
            if (_go == null) { _go = new GameObject("DarkwoodMP.DogWatch"); Object.DontDestroyOnLoad(_go); _go.AddComponent<Watch>(); }
        }

        private static void Stop() { On = false; if (_go != null) Object.Destroy(_go); _go = null; }

        private static string List()
        {
            var c0 = MapScenario.Centre;
            var parts = new List<string>();
            foreach (var c in Object.FindObjectsOfType<Character>())
            {
                if (c == null || c.npc != null || !c.alive) continue;
                var p = c.transform.position;
                if (StateDump.Flat(p - c0) > 3000f) continue;
                parts.Add($"{c.name}@{p.x - c0.x:0},{p.z - c0.z:0}{(c.sleeping ? " asleep" : "")}{(c.eating ? " eating" : "")}{(c.playingCustomAni ? " ani:" + c.customAni : "")}");
            }
            Plugin.Log.LogInfo("[dogwatch] list: " + string.Join("; ", parts));
            return parts.Count + " enemies: " + string.Join("; ", parts.Take(30));
        }

        internal static string Id(Character c) => NetEnemies.Instance != null && NetEnemies.Instance.NetIdOf(c, out uint id) ? id.ToString() : "i" + c.GetInstanceID();

        internal static string Rel(Vector3 p) { var c = MapScenario.Centre; return $"{p.x - c.x:0},{p.z - c.z:0}"; }

        /// <summary>How far from each player (host's own and the proxies), as "host 120 / client 45 (proxy at x,z)".</summary>
        internal static string Dists(Vector3 p)
        {
            var parts = new List<string>();
            if (Player.Instance != null) parts.Add($"me {StateDump.Flat(Player.Instance.transform.position - p):0}");
            foreach (var px in NetPlayerProxy.All.Values)
                if (px != null) parts.Add($"{px.OwnerName} {StateDump.Flat(px.transform.position - p):0} (at {Rel(px.transform.position)})");
            return string.Join(" / ", parts);
        }

        internal static string Caller()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            var names = new List<string>();
            for (int i = 0; i < st.FrameCount && names.Count < 5; i++)
            {
                var m = st.GetFrame(i)?.GetMethod();
                if (m == null) continue;
                string t = m.DeclaringType != null ? m.DeclaringType.Name : "?";
                if (t.Contains("Patch") || t == "MapScenarioDogWatch") continue;
                names.Add(t + "." + m.Name);
            }
            return string.Join(" < ", names);
        }

        internal static bool Watched(Character c)
        {
            if (!On || c == null || c.npc != null) return false;
            var s = Net.Session.Instance;
            if (s == null || !s.IsHost) return false;
            return StateDump.Flat(c.transform.position - MapScenario.Centre) < 3000f;
        }

        private sealed class Watch : MonoBehaviour
        {
            private struct St { public bool Sleep, Waking, Eat, Alive, Ani, Atk; public Character.Behaviour B; public Transform T; }
            private static readonly Dictionary<Character, St> _last = new Dictionary<Character, St>();
            private static float _next, _nextScan, _nextPos;
            private static List<Character> _list = new List<Character>();
            public static int Wakes, Alerts, Changes, Close;
            private static readonly HashSet<uint> _closeNow = new HashSet<uint>();

            public static void Reset() { _last.Clear(); _list.Clear(); Wakes = Alerts = Changes = Close = 0; _closeNow.Clear(); _next = _nextScan = _nextPos = 0f; }
            public static void Woke() => Wakes++;
            public static void Alerted() => Alerts++;
            public static string Summary() => $"wakes {Wakes}, alerts {Alerts}, state changes {Changes}, close contacts {Close}";

            private void Update()
            {
                if (!On || Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.1f;
                var s = Net.Session.Instance;
                if (s == null) return;
                if (s.IsHost) HostTick(); else ClientTick();
            }

            private static void HostTick()
            {
                if (Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + 3f;
                    _list = Object.FindObjectsOfType<Character>().Where(c => c != null && c.npc == null && StateDump.Flat(c.transform.position - MapScenario.Centre) < 3000f).ToList();
                }
                foreach (var c in _list)
                {
                    if (c == null) continue;
                    var st = new St { Sleep = c.sleeping, Waking = c.wakingUp, Eat = c.eating, Alive = c.alive, Ani = c.playingCustomAni, Atk = c.attacking, B = c.behaviour, T = c.target };
                    if (_last.TryGetValue(c, out var o) && o.Sleep == st.Sleep && o.Waking == st.Waking && o.Eat == st.Eat && o.Alive == st.Alive && o.Ani == st.Ani && o.Atk == st.Atk && (o.B == st.B || o.Eat && st.Eat) && o.T == st.T) continue;
                    bool first = !_last.ContainsKey(c);
                    _last[c] = st;
                    if (first) continue;
                    Changes++;
                    Plugin.Log.LogInfo($"[dogwatch] {c.name}#{Id(c)} at {Rel(c.transform.position)}: {(st.Alive ? "" : "dead ")}{(st.Sleep ? "asleep " : "")}{(st.Waking ? "waking " : "")}{(st.Eat ? "eating " : "")}{(st.Ani ? "ani:" + c.customAni + " " : "")}{(st.Atk ? "attacking:" + (c.currentAttack != null ? c.currentAttack.name : "?") + " " : "")}{st.B} -> {(st.T != null ? st.T.name : "-")}; {Dists(c.transform.position)}");
                }
                if (Time.unscaledTime >= _nextPos)
                {
                    _nextPos = Time.unscaledTime + 1f;
                    foreach (var px in NetPlayerProxy.All.Values)
                        if (px != null) Plugin.Log.LogInfo($"[dogwatch] t {Time.unscaledTime:0.0}: proxy {px.OwnerName} at {Rel(px.transform.position)}, me at {(Player.Instance != null ? Rel(Player.Instance.transform.position) : "-")}");
                }
            }

            private static void ClientTick()
            {
                var ne = NetEnemies.Instance;
                var me = Player.Instance;
                if (ne == null || me == null) return;
                var at = me.transform.position;
                bool log = Time.unscaledTime >= _nextPos;
                if (log) _nextPos = Time.unscaledTime + 1f;
                var near = new List<string>();
                foreach (var c in ne.PuppetCharacters())
                {
                    float d = StateDump.Flat(c.transform.position - at);
                    ne.NetIdOf(c, out uint id);
                    if (d < 45f) { if (_closeNow.Add(id)) { Close++; Plugin.Log.LogInfo($"[dogwatch] our player touches the host's {c.name} #{id}: {d:0} apart, it at {Rel(c.transform.position)}{(c.sleeping ? " asleep" : "")}, we at {Rel(at)}"); } }
                    else _closeNow.Remove(id);
                    if (d < 400f) near.Add($"{c.name}#{id} {d:0}{(c.sleeping ? " asleep" : "")}");
                }
                if (log && near.Count > 0) Plugin.Log.LogInfo($"[dogwatch] t {Time.unscaledTime:0.0}: we at {Rel(at)}; near: {string.Join(", ", near)}");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.wakeup))]
        static class WakePatch
        {
            static void Prefix(Character __instance)
            {
                if (!Watched(__instance) || !__instance.sleeping || __instance.wakingUp) return;
                Watch.Woke();
                Plugin.Log.LogInfo($"[dogwatch] WAKE {__instance.name}#{Id(__instance)} at {Rel(__instance.transform.position)} by {Caller()}; noise source {(Threat.NoiseSource != null ? Threat.NoiseSource.name : "-")}; {Dists(__instance.transform.position)}");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.beAlerted))]
        static class AlertPatch
        {
            static void Prefix(Character __instance, Transform destTarget, bool isAggressiveAction)
            {
                if (!Watched(__instance) || __instance.target == destTarget) return;
                Watch.Alerted();
                Plugin.Log.LogInfo($"[dogwatch] ALERT {__instance.name}#{Id(__instance)} at {Rel(__instance.transform.position)}{(__instance.sleeping ? " asleep" : "")}{(__instance.eating ? " eating" : "")} at {(destTarget != null ? destTarget.name : "-")} aggressive {isAggressiveAction} by {Caller()}; {Dists(__instance.transform.position)}");
            }
        }

        private static readonly Dictionary<int, float> _heardAt = new Dictionary<int, float>();

        [HarmonyPatch(typeof(Character), nameof(Character.heardSound), new[] { typeof(Vector3), typeof(float), typeof(bool), typeof(float), typeof(bool) })]
        static class HearPatch
        {
            static void Prefix(Character __instance, out float __state) => __state = __instance != null ? __instance.hearingCounter : 0f;

            static void Postfix(Character __instance, Vector3 Position, float soundRange, bool dangerousSound, float volume, float __state)
            {
                if (!Watched(__instance)) return;
                bool crossed = __instance.hearingCounter >= 1f && __state < 1f || dangerousSound;
                int id = __instance.GetInstanceID();
                if (!crossed && _heardAt.TryGetValue(id, out var t) && Time.unscaledTime - t < 2f) return;
                _heardAt[id] = Time.unscaledTime;
                Plugin.Log.LogInfo($"[dogwatch] HEARD {__instance.name}#{Id(__instance)}{(__instance.sleeping ? " asleep" : "")}{(__instance.eating ? " eating" : "")} {(crossed ? "LOUD " : "")}counter {__state:0.00}->{__instance.hearingCounter:0.00} from {Rel(Position)} ({StateDump.Flat(Position - __instance.transform.position):0} away) range {soundRange:0} vol {volume:0.00} danger {dangerousSound} source {(Threat.NoiseSource != null ? Threat.NoiseSource.name : "-")} by {Caller()}");
            }
        }
    }
}
