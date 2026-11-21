using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Loading
{
    /// <summary>
    /// Makes loading a saved world faster without changing what it loads.
    /// Measured with --dwmp-profile-load, most of the time went to three things
    /// the game does slowly, not to work it has to do:
    ///
    /// 1. After making the saved objects, SaveManager.Load walks its
    ///    21,000-entry loadedObjects dictionary with Values.ElementAt(i) and
    ///    Keys.ElementAt(i). Those can't index a dictionary, so each call
    ///    counts from the start: about 450 million steps. Here they read from
    ///    an array copy of each collection instead (made again whenever its
    ///    size changes, and dropped when the load ends).
    /// 2. WorldGrid.registerToNode, called for each of about 8,000 objects as a
    ///    grid registers them on load, looks through all of the grid's objects
    ///    to see if it's already listed, and through all of its nodes to find
    ///    the one it's in. While a grid registers its objects, both are looked
    ///    up in an index instead; the node found is the same one (the first in
    ///    the grid's list that the object is in).
    /// 3. Load reads the save's two JSON files (about 11 MB) and parses them on
    ///    the main thread, about 1.3 s, right after the game's fixed 1.6 s wait
    ///    (below). They're parsed on a background thread during that wait
    ///    instead, and Load takes the result if the text it read is the same;
    ///    otherwise, or if the background parse failed, it parses as before.
    /// 4. Core.AddPrefab(path) calls Resources.Load for each of about 19,000
    ///    saved objects, though there are only a few hundred kinds. While a
    ///    world loads, each path's prefab is looked up once.
    ///
    /// (The game's fixed 1.6 s wait before loading is left alone: it lets the
    /// black screen finish fading out before Load stops every running tween,
    /// DOTween.KillAll. Cut short, the fade stops half-way and the whole game
    /// stays dim.)
    /// </summary>
    public sealed partial class LoadSpeed
    {
        /// The one ModHost keeps, for the patches.
        internal static LoadSpeed Current => Plugin.Instance.Host.LoadSpeed;

        // ---- 1. ElementAt on the dictionary's keys and values ---------------

        private readonly Dictionary<object, (int count, Array copy)>
            _copies = new Dictionary<object, (int, Array)>();

        // The calls the transpiler puts in the game's code: they must be
        // static, so they pass on to the one ModHost keeps.
        public static T ElementAt<T>(IEnumerable<T> source, int index)
        {
            return Current.Indexed(source, index);
        }

        public static SaveManager.DynamicSave Dynamic(string text) =>
            Current.Take(ref Current._dynamic, text);
        public static SaveManager.StaticSave Static(string text) =>
            Current.Take(ref Current._static, text);

        private T Indexed<T>(IEnumerable<T> source, int index)
        {
            if (!(source is ICollection<T> c))
            {
                return Enumerable.ElementAt(source, index);
            }

            if (!_copies.TryGetValue(source, out var e) || e.count != c.Count
                || !(e.copy is T[]))
            {
                _copies[source] = e = (c.Count, source.ToArray());
            }

            return ((T[])e.copy)[index];
        }


        // ---- 3. the save's JSON, parsed while the screen fades --------------

        private sealed class Ahead<T> where T : class
        {
            public string Path, Text;
            public T Result;
            public System.Threading.Thread Thread;
        }

        private Ahead<SaveManager.DynamicSave> _dynamic;
        private Ahead<SaveManager.StaticSave> _static;
        private int AheadUsed, AheadMissed;

        /// <summary>
        /// The game is about to load the current profile's world: start parsing
        /// its files.
        /// </summary>
        internal void ParseAhead()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return;
            }

            _dynamic = Start<SaveManager.DynamicSave>(sm.dynamicFile);
            _static = Start<SaveManager.StaticSave>(sm.staticFile);
        }

        private Ahead<T> Start<T>(string path) where T : class
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                return null;
            }

            var a = new Ahead<T> { Path = path };
            a.Thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // Shared for writing and deleting: on Windows a plain read
                    // would make the game's own write of this file fail while
                    // the read lasts. If the game changes it meanwhile, Take
                    // sees other text.
                    using (var fs = new System.IO.FileStream(
                        path, System.IO.FileMode.Open,
                        System.IO.FileAccess.Read,
                        System.IO.FileShare.ReadWrite
                            | System.IO.FileShare.Delete))
                    {
                        using (var sr = new System.IO.StreamReader(
                            fs, System.Text.Encoding.UTF8,
                            detectEncodingFromByteOrderMarks: true))
                        {
                            a.Text = sr.ReadToEnd();
                        }
                    }

                    a.Result =
                        Newtonsoft.Json.JsonConvert.DeserializeObject<T>(
                            a.Text);
                }
                catch
                {
                    // encrypted, or something that needs the main thread: Load
                    // parses it
                    a.Result = null;
                }
            })
            {
                IsBackground = true,
                Name = "DarkwoodMP save parse"
            };

            a.Thread.Start();
            return a;
        }

        private T Take<T>(ref Ahead<T> slot, string text) where T : class
        {
            // each parse is used once: a later Load (the backup file) parses
            // itself
            var a = slot;
            slot = null;
            if (a != null)
            {
                a.Thread.Join();
                if (a.Result != null
                    && string.Equals(a.Text, text, StringComparison.Ordinal))
                {
                    AheadUsed++;
                    return a.Result;
                }

                AheadMissed++;
            }

            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(text);
        }

        // ---- 4. Resources.Load once per prefab path while loading -----------

        internal bool Loading;
        private readonly Dictionary<string, UnityEngine.Object>
            _prefabs = new Dictionary<string, UnityEngine.Object>();
        private int PrefabLookups, PrefabLoads;

        internal UnityEngine.Object Prefab(string path)
        {
            PrefabLookups++;
            if (!_prefabs.TryGetValue(path, out var o) || o == null)
            {
                _prefabs[path] = o = Resources.Load("Prefabs/" + path);
                PrefabLoads++;
            }

            return o;
        }

        internal void LoadStarted()
        {
            Loading = true;
            PrefabLookups = PrefabLoads = 0;
        }

        internal void LoadEnded()
        {
            Loading = false;
            _copies.Clear();
            // not held after loading, so the game can unload what it no longer
            // uses
            _prefabs.Clear();
            Plugin.Log.LogInfo(
                $"LoadSpeed: {PrefabLookups} prefabs made from {PrefabLoads} "
                    + $"loads; save parsed ahead: {AheadUsed} used, "
                    + $"{AheadMissed} not");
            AheadUsed = AheadMissed = 0;
        }

        // ---- what the patches hand over (LoadSpeedPatches) ----------------

        /// SaveManager.Load's coroutine is about to step.
        internal void LoadStepping()
        {
            if (!Loading)
            {
                LoadStarted();
            }
        }

        /// SaveManager.Load's coroutine stepped: false when it's done.
        internal void LoadStepped(bool more)
        {
            if (!more)
            {
                LoadEnded();
            }
        }

        /// WorldGenerator.Start ran: a saved world is parsed meanwhile.
        internal void WorldStarted()
        {
            if (Core.loadingGame)
            {
                ParseAhead();
            }
        }

        /// SaveManager.Load's loop with its ElementAt calls and its JSON
        /// parses replaced by ElementAt, Dynamic and Static here.
        internal static IEnumerable<CodeInstruction> Rewrite(
            IEnumerable<CodeInstruction> code)
        {
            var fast = AccessTools.Method(
                typeof(LoadSpeed), nameof(LoadSpeed.ElementAt));
            var dyn = AccessTools.Method(
                typeof(LoadSpeed), nameof(LoadSpeed.Dynamic));
            var stat =
                AccessTools.Method(typeof(LoadSpeed), nameof(LoadSpeed.Static));
            int n = 0, parses = 0;
            foreach (var ins in code)
            {
                if ((ins.opcode == OpCodes.Call
                        || ins.opcode == OpCodes.Callvirt)
                    && ins.operand is MethodInfo m)
                {
                    if (m.Name == "ElementAt"
                        && m.DeclaringType == typeof(Enumerable)
                        && m.IsGenericMethod)
                    {
                        ins.opcode = OpCodes.Call;
                        ins.operand =
                            fast.MakeGenericMethod(m.GetGenericArguments());
                        n++;
                    }
                    // JsonConvert.DeserializeObject<T>(string), where T is
                    // DynamicSave or StaticSave: the parse made ahead.
                    else if (m.Name == "DeserializeObject"
                        && m.DeclaringType
                            == typeof(Newtonsoft.Json.JsonConvert)
                        && m.IsGenericMethod
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(string))
                    {
                        var t = m.GetGenericArguments()[0];
                        if (t == typeof(SaveManager.DynamicSave))
                        {
                            ins.operand = dyn;
                            parses++;
                        }
                        else if (t == typeof(SaveManager.StaticSave))
                        {
                            ins.operand = stat;
                            parses++;
                        }
                    }
                }

                yield return ins;
            }

            if (n == 0)
            {
                Plugin.Log.LogWarning(
                    "LoadSpeed: SaveManager.Load has no ElementAt calls to "
                        + "speed up (game changed?)");
            }

            if (parses == 0)
            {
                Plugin.Log.LogWarning(
                    "LoadSpeed: SaveManager.Load parses no save files (game "
                        + "changed?)");
            }
        }

        /// WorldGrid.registerToNodes during a batch: false when it was done
        /// here, true for the game's own.
        internal bool GameRegistersToNodes(
            WorldGrid w, GameObject go, Vector2 size,
            WorldGrid.Cullable.Type type)
        {
            if (!Batch)
            {
                return true;
            }

            try
            {
                return RegisterToNodes(w, go, size, type);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"LoadSpeed: {e.Message}; the game's own registerToNodes "
                        + "instead");
                EndBatch();
                return true;
            }
        }

        /// WorldGrid.registerToNode during a batch, as GameRegistersToNodes.
        internal bool GameRegistersToNode(
            WorldGrid w, GameObject go, WorldGrid.Cullable.Type type)
        {
            if (!Batch)
            {
                return true;
            }

            try
            {
                return RegisterToNode(w, go, type);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"LoadSpeed: {e.Message}; the game's own registerToNode "
                        + "instead");
                EndBatch();
                return true;
            }
        }

        /// WorldGrid.registerToClosestNode, as GameRegistersToNodes.
        internal bool GameRegistersToClosestNode(WorldGrid w, GameObject go)
        {
            return !Batch || RegisterToClosestNode(w, go);
        }
    }
}
