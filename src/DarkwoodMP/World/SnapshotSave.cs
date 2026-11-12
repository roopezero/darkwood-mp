using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The host's snapshot of sav.dat for a joiner, split in two. The game's own SaveManager.Save reads the world into
    /// plain save states (the player, the clock, flags, every dynamic object's SavedObj) and then writes them out as
    /// JSON, all on the main thread; the writing is most of its time. Here the reading is done the same way, in one
    /// frame (so the snapshot is still one moment of the world), and the JSON is written on a worker thread while the
    /// game goes on. The file is the same as the game's: the same fields in the same order, with the game's own
    /// SavedObj.save for the objects.
    ///
    /// Used only when the static part (savs.dat) is copied from the host's save, so the game's save would not have
    /// written it either. If anything here fails (a member the game no longer has, a list the game changed while it
    /// was being written), the caller saves the snapshot the game's way instead.
    /// </summary>
    internal static class SnapshotSave
    {
        /// <summary>
        /// Main thread: reads the world as SaveManager.Save does (with force: the night-time check is passed) and
        /// starts writing it to <paramref name="file"/> on a worker thread. The task's result is how long the writing
        /// took, in ms.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)] // a missing game member then fails the call, inside the caller's try
        public static Task<long> Begin(SaveManager sm, string file)
        {
            sm.updateFilePaths();
            sm.saveGameProfiles(); // also sets the profile's day, chapter and time saved, which go to the joiner
            sm.lastTimeSaved = DateTime.Now;
            sm.haveSaved = true;
            if (File.Exists(file)) File.Delete(file);
            sm.assignUniqueIds(sm.dynamicObjects);

            var d = new SaveManager.DynamicSave
            {
                majorVersion = Core.majorVersion,
                minorVersion = Core.minorVersion,
                majorVersionCompatibility = Core.majorVersionCompatibility,
                minorVersionCompatibility = Core.minorVersionCompatibility,
                RCVersion = Core.RCVersion,
                RCVersionCompatibility = Core.RCVersionCompatibility,
                saveMS = new SaveManager.SaveState(sm),
                pS = new Player.SaveState(Player.Instance),
                iS = new Inventory.SaveState(Player.Instance.Inventory),
                hS = new Inventory.SaveState(Player.Instance.Hotbar),
                cnS = new Controller.SaveState(Singleton<Controller>.Instance),
                drS = new Dreams.SaveState(Singleton<Dreams>.Instance),
                outS = new OutsideLocations.SaveState(Singleton<OutsideLocations>.Instance),
                raS = new Rain.SaveState(Singleton<Rain>.Instance),
                flaS = new Flags.SaveState(Singleton<Flags>.Instance),
                joS = new Journal.SaveState(Singleton<UI>.Instance.journal),
                numS = new global::RandomNumberGenerator.SaveState(Singleton<global::RandomNumberGenerator>.Instance),
                evS = new Events.SaveState(Singleton<Events>.Instance),
                chspS = new CharacterSpawner.SaveState(Singleton<CharacterSpawner>.Instance),
            };
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg != null)
            {
                d.worldGenS = new WorldGenerator.SaveState(wg);
                d.gridS = new WorldGrid.SaveState(wg);
                d.bbdS = new WorldGenerator.BigBiomeDynamicStates(wg);
            }
            d.mapS = new Map.SaveState(Singleton<Map>.Instance);
            d.nightScenS = new NightScenarios.SaveState(Singleton<NightScenarios>.Instance);
            d.chDC = new CharacterDifficultyController.SaveState(Singleton<CharacterDifficultyController>.Instance);
            var objs = sm.dynamicObjects;
            for (int i = 0; i < objs.Count; i++)
                if (objs[i] != null) d.savedObjs.Add(new SaveManager.SavedObj(objs[i].gameObject));

            // Private in the game; it sets it to None on start (SaveManager.Awake).
            var formatting = HarmonyLib.Traverse.Create(sm).Field("formatting").GetValue() is Formatting f ? f : Formatting.None;
            return Task.Run(() => Write(d, file, formatting));
        }

        /// <summary>Worker thread: sav.dat as the game writes it (SaveManager.Save, its StreamWriter way).</summary>
        private static long Write(SaveManager.DynamicSave d, string file, Formatting formatting)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var serializer = new JsonSerializer();
            var parts = new List<(string, object)>
            {
                ("majorVersion", d.majorVersion), ("minorVersion", d.minorVersion),
                ("majorVersionCompatibility", d.majorVersionCompatibility), ("minorVersionCompatibility", d.minorVersionCompatibility),
                ("RCVersion", d.RCVersion), ("RCVersionCompatibility", d.RCVersionCompatibility),
                ("saveMS", d.saveMS), ("pS", d.pS), ("iS", d.iS), ("hS", d.hS), ("cnS", d.cnS), ("drS", d.drS),
                ("outS", d.outS), ("raS", d.raS), ("flaS", d.flaS), ("joS", d.joS), ("numS", d.numS), ("evS", d.evS),
                ("chspS", d.chspS), ("worldGenS", d.worldGenS), ("gridS", d.gridS), ("bbdS", d.bbdS), ("mapS", d.mapS),
                ("nightScenS", d.nightScenS), ("chDC", d.chDC),
            };
            string part = file + ".part"; // a failed write never leaves a sav.dat that looks whole
            try
            {
                using (var text = new StreamWriter(part))
                using (var w = new JsonTextWriter(text))
                {
                    w.Formatting = formatting;
                    w.WriteStartObject();
                    foreach (var (name, value) in parts)
                    {
                        w.WritePropertyName(name);
                        serializer.Serialize(w, value);
                    }
                    w.WritePropertyName("savedObjs");
                    w.WriteStartArray();
                    foreach (var o in d.savedObjs) o.save(serializer, w);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                File.Move(part, file);
            }
            finally
            {
                if (File.Exists(part)) File.Delete(part);
            }
            return sw.ElapsedMilliseconds;
        }
    }
}
