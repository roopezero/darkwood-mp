using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What the mod knows about your save slots, beside the game's own profiles: a name you gave the save, and who
    /// has played it with you (each player who joined while you hosted it, by name and player key, with when they
    /// last did). The Start Game window shows it.
    ///
    /// It's kept in the mod's own file, BepInEx/config/DarkwoodMP.saves.json, never in the save folder. While the
    /// saves are a test copy (--dwmp-sandbox DIR) it's DIR/darkwoodmp-saves.json instead, so tests never touch the
    /// real one. A client's copy of a host's world (and the test world) has no record.
    ///
    /// A record belongs to one game in one slot: it's started empty when a new game starts in that slot
    /// (UI.initNewGame with a profile that isn't active yet) and dropped when the game deletes the slot
    /// (SaveManager.deleteCurrentProfile: Delete, a permadeath, a new game over it). Each record notes when it
    /// was started.
    /// </summary>
    public static class SaveBook
    {
        public sealed class Player
        {
            public string Name = "";
            public string Key = "";
            public string Last = "";
        }

        public sealed class Record
        {
            public string Name = "";
            public string Started = "";
            public List<Player> Players = new List<Player>();
        }

        private sealed class FileData
        {
            public Dictionary<string, Record> Slots = new Dictionary<string, Record>();
        }

        private static FileData _data;
        private static string _loadedFrom;
        private static DateTime _stamp;

        /// <summary>The file for the saves the game uses now, or null when there's none (a host's world in the cache).</summary>
        public static string FilePath
        {
            get
            {
                if (!SaveRedirect.Active) return Path.Combine(BepInEx.Paths.ConfigPath, "DarkwoodMP.saves.json");
                if (WorldShare.Instance?.PlayingHostCopy != false) return null;
                return Path.Combine(SaveRedirect.Dir, "darkwoodmp-saves.json");
            }
        }

        public static void Register(Session session)
        {
            session.OnPeerConnected += id =>
            {
                if (!session.IsHost || Core.currentProfile == null) return;
                string name = session.NameOf(id);
                session.Keys.TryGetValue(id, out string key);
                AddPlayer(Core.currentProfile.id, name, key ?? "");
            };
        }

        private static FileData Data()
        {
            string path = FilePath;
            if (path == null) return null;
            // Read again when the file changed or went away underneath (a test's fresh copy of the saves).
            DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (_data != null && _loadedFrom == path && _stamp == stamp) return _data;
            _loadedFrom = path;
            _stamp = stamp;
            _data = new FileData();
            try
            {
                if (File.Exists(path)) _data = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path)) ?? new FileData();
                if (_data.Slots == null) _data.Slots = new Dictionary<string, Record>();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[saves] couldn't read {path}: {e.Message}"); }
            return _data;
        }

        private static void Write()
        {
            string path = FilePath;
            if (path == null || _data == null || _loadedFrom != path) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_data, Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _stamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[saves] couldn't write {path}: {e.Message}"); }
        }

        /// <summary>The record for slot <paramref name="slot"/>, or null if there's none.</summary>
        public static Record Get(int slot)
        {
            var d = Data();
            return d != null && d.Slots.TryGetValue(slot.ToString(), out var r) ? r : null;
        }

        private static Record GetOrAdd(int slot)
        {
            var d = Data();
            if (d == null) return null;
            if (!d.Slots.TryGetValue(slot.ToString(), out var r))
                d.Slots[slot.ToString()] = r = new Record { Started = Now() };
            return r;
        }

        private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        /// <summary>The name you gave the save in slot <paramref name="slot"/> ("" for none).</summary>
        public static string NameOf(int slot) => Get(slot)?.Name ?? "";

        public static void SetName(int slot, string name)
        {
            name = (name ?? "").Trim();
            if (NameOf(slot) == name) return;
            var r = GetOrAdd(slot);
            if (r == null) return;
            r.Name = name;
            Write();
            Plugin.Log.LogInfo($"[saves] slot {slot} named \"{name}\"");
        }

        /// <summary>
        /// The names of those who've played slot <paramref name="slot"/> with you, most recent first. A save hosted
        /// before the mod kept these records has none: then the names come from the characters the host kept for
        /// the players who joined it (prof&lt;id&gt;/darkwoodmp_players).
        /// </summary>
        public static List<string> PlayedWith(int slot)
        {
            var r = Get(slot);
            if (r != null) return r.Players.OrderByDescending(p => p.Last, StringComparer.Ordinal).Select(p => p.Name).ToList();
            var names = new List<string>();
            try
            {
                var sm = Singleton<SaveManager>.Instance;
                string dir = sm == null ? null : Path.Combine(Path.Combine(sm.baseSaveDirectory, "prof" + slot), "darkwoodmp_players");
                if (FilePath != null && dir != null && Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
                    {
                        var c = JsonConvert.DeserializeObject<Characters.Record>(File.ReadAllText(f));
                        if (c != null && !string.IsNullOrEmpty(c.Name) && !names.Contains(c.Name)) names.Add(c.Name);
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[saves] couldn't read slot {slot}'s players' characters: {e.Message}"); }
            return names;
        }

        public static void AddPlayer(int slot, string name, string key)
        {
            var r = GetOrAdd(slot);
            if (r == null) return;
            var p = r.Players.FirstOrDefault(x => key != "" ? x.Key == key : x.Name == name);
            if (p == null) r.Players.Add(p = new Player { Key = key });
            p.Name = name;
            p.Last = Now();
            Write();
            Plugin.Log.LogInfo($"[saves] slot {slot}: played with {name}");
        }

        /// <summary>A new game in slot <paramref name="slot"/>, or the slot deleted: its record goes.</summary>
        public static void Forget(int slot, string why)
        {
            var d = Data();
            if (d == null || !d.Slots.Remove(slot.ToString())) return;
            Write();
            Plugin.Log.LogInfo($"[saves] slot {slot}'s record dropped ({why})");
        }
    }

    /// <summary>A new game starts in a slot (the profile isn't active yet): its record starts empty.</summary>
    [HarmonyPatch(typeof(UI), nameof(UI.initNewGame))]
    static class SaveBookNewGamePatch
    {
        static void Prefix()
        {
            var p = Core.currentProfile;
            if (p == null || p.Active) return;
            SaveBook.Forget(p.id, "a new game");
            // The characters the host kept for the players who joined the slot's last world: the game writes the new
            // world over the old save and leaves them, so a joiner would get their old character (dead, if it died
            // for good). They go with the old world, as when the slot is deleted.
            Characters.Instance?.Forget("a new game");
            try
            {
                var sm = Singleton<SaveManager>.Instance;
                string dir = sm == null || WorldShare.Instance?.PlayingHostCopy == true ? null
                    : Path.Combine(Path.Combine(sm.baseSaveDirectory, "prof" + p.id), "darkwoodmp_players");
                if (dir != null && Directory.Exists(dir)) { Directory.Delete(dir, true); Plugin.Log.LogInfo($"[saves] slot {p.id}'s players' characters deleted (a new game)"); }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[saves] couldn't delete slot {p.id}'s players' characters: {e.Message}"); }
        }
    }

    /// <summary>
    /// The game deletes a slot's save: its record goes too, and so do the characters the host kept for the players
    /// who joined it (prof&lt;id&gt;/darkwoodmp_players, World.Characters), which the game's deleteSave leaves behind:
    /// otherwise a new game in that slot would hand them their old characters.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.deleteCurrentProfile))]
    static class SaveBookDeletePatch
    {
        static void Prefix(SaveManager __instance)
        {
            var p = Core.currentProfile;
            if (p == null || WorldShare.Instance?.PlayingHostCopy == true) return;
            SaveBook.Forget(p.id, "deleted");
            Characters.Instance?.Forget("the slot deleted");
            try
            {
                string dir = Path.Combine(Path.Combine(__instance.baseSaveDirectory, "prof" + p.id), "darkwoodmp_players");
                if (Directory.Exists(dir)) { Directory.Delete(dir, true); Plugin.Log.LogInfo($"[saves] slot {p.id}'s players' characters deleted with it"); }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[saves] couldn't delete slot {p.id}'s players' characters: {e.Message}"); }
        }
    }
}
