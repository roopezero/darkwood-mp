using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// "--dwmp-profile-load": times the game's own loading code, to find what makes loading a world slow. Every
    /// method of the classes that load a world (and the steps of their coroutines) gets a stopwatch; once the
    /// world is ready, the most expensive ones are logged with their total and longest single call, and so are
    /// the frames that took longest. A debugging aid: it slows the game down while it's on.
    /// </summary>
    public static class LoadProfiler
    {
        private static readonly string[] TypeNames =
        {
            "SaveManager", "WorldGenerator", "UI", "Core", "WorldGrid", "OutsideLocations", "Controller", "Location",
            "WorldChunk", "Flags", "Dreams", "Player", "CharacterSpawner", "Inventory", "AstarPath", "MainMenu",
        };

        /// <summary>Called millions of times while loading: timing them would cost more than they do.</summary>
        private static readonly HashSet<string> Hot = new HashSet<string>
        {
            "trueDistance", "toVector2", "InitializeNode", "GetNewNodeIndex", "worldGenFinished", "cursorPos",
            "addToLoadedTextures", "removeFromLoadedTextures", "leave", "hide", "show",
        };

        private sealed class Stat { public double Total, Max; public int Calls; }
        private static readonly Dictionary<MethodBase, Stat> _stats = new Dictionary<MethodBase, Stat>();
        private static readonly List<(int frame, float ms, float at)> _longFrames = new List<(int, float, float)>();
        private static bool _on;
        private static float _lastFrame;

        public static void Start(Harmony harmony)
        {
            _on = true;
            var prefix = new HarmonyMethod(typeof(LoadProfiler), nameof(Before));
            var postfix = new HarmonyMethod(typeof(LoadProfiler), nameof(After));
            int n = 0;
            foreach (var name in TypeNames)
            {
                var type = AccessTools.TypeByName(name);
                if (type == null) continue;
                var types = new List<Type> { type };
                types.AddRange(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public));
                foreach (var t in types)
                {
                    if (t.IsGenericTypeDefinition) continue;
                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsAbstract || m.IsGenericMethodDefinition || m.GetMethodBody() == null) continue;
                        if (m.Name == "Update" || m.Name == "LateUpdate" || m.Name == "FixedUpdate" || m.Name == "OnGUI") continue;
                        if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || Hot.Contains(m.Name)) continue;
                        if (t != type && t.Name.StartsWith("<>c") && type == typeof(AstarPath)) continue;
                        try { harmony.Patch(m, prefix, postfix); n++; }
                        catch { }
                    }
                }
            }
            Plugin.Log.LogInfo($"[profile] timing {n} methods");
        }

        public static void Frame()
        {
            if (!_on) return;
            float now = Time.realtimeSinceStartup;
            float ms = (now - _lastFrame) * 1000f;
            if (_lastFrame > 0f && ms > 150f) _longFrames.Add((Time.frameCount, ms, now));
            _lastFrame = now;
        }

        private static void Before(out long __state) => __state = Stopwatch.GetTimestamp();

        private static void After(MethodBase __originalMethod, long __state)
        {
            double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
            if (!_stats.TryGetValue(__originalMethod, out var s)) _stats[__originalMethod] = s = new Stat();
            s.Total += ms; s.Calls++; if (ms > s.Max) s.Max = ms;
        }

        public static void Report()
        {
            if (!_on) return;
            Plugin.Log.LogInfo("[profile] slowest methods (inclusive: a method's time includes what it calls):");
            foreach (var kv in _stats.OrderByDescending(k => k.Value.Total).Take(60))
                Plugin.Log.LogInfo($"[profile] {kv.Value.Total,9:0} ms total, {kv.Value.Max,8:0} ms longest, {kv.Value.Calls,7} calls  {kv.Key.DeclaringType?.FullName}.{kv.Key.Name}");
            foreach (var f in _longFrames.OrderByDescending(f => f.ms).Take(25))
                Plugin.Log.LogInfo($"[profile] frame {f.frame} took {f.ms:0} ms (ended at {f.at:0.0} s)");
            Plugin.Log.LogInfo($"[profile] frames over 150 ms: {_longFrames.Count}, {_longFrames.Sum(f => f.ms) / 1000f:0.0} s in all");
        }
    }
}
