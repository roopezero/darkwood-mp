using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World.KeptCharacters;
using DarkwoodMP.World.Saves;
using Newtonsoft.Json;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What the mod knows about your save slots, beside the game's own
    /// profiles: a name you gave the save, and who has played it with you (each
    /// player who joined while you hosted it, by name and player key, with when
    /// they last did). The Start Game window shows it.
    ///
    /// It's kept in the mod's own file, BepInEx/config/DarkwoodMP.saves.json,
    /// never in the save folder. While the saves are a test copy
    /// (--dwmp-sandbox DIR) it's DIR/darkwoodmp-saves.json instead, so tests
    /// never touch the real one. A client's copy of a host's world (and the
    /// test world) has no record.
    ///
    /// A record belongs to one game in one slot: it's started empty when a new
    /// game starts in that slot (UI.initNewGame with a profile that isn't
    /// active yet) and dropped when the game deletes the slot
    /// (SaveManager.deleteCurrentProfile: Delete, a permadeath, a new game over
    /// it). Each record notes when it was started.
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

        /// <summary>
        /// The file for the saves the game uses now, or null when there's none
        /// (a host's world in the cache).
        /// </summary>
        public static string FilePath => RedirectRules.BookFile(
            BepInEx.Paths.ConfigPath,
            Plugin.Instance.Host.SaveFolders.Dir,
            HostCopy());

        /// <summary>
        /// Whether we play a copy of a host's world, or null when that isn't
        /// known (no WorldShare yet).
        /// </summary>
        internal static bool? HostCopy()
        {
            WorldShare share = Plugin.Instance.Host.Share;
            if (share == null)
            {
                return null;
            }

            return share.PlayingHostCopy;
        }

        /// <summary>
        /// For as long as the session runs, a host notes each player who
        /// joins in the slot's record.
        /// </summary>
        internal static void Watch(CoopSession coop)
        {
            Session session = coop.Net;
            Action<ulong> onPeer = id => OnPeer(session, id);
            session.OnPeerConnected += onPeer;
            coop.Hook(() => session.OnPeerConnected -= onPeer);
        }

        private static void OnPeer(Session session, ulong id)
        {
            if (!session.IsHost || Core.currentProfile == null)
            {
                return;
            }

            string name = session.NameOf(id);
            session.Keys.TryGetValue(id, out string key);
            string stored = key;
            if (stored == null)
            {
                stored = "";
            }

            AddPlayer(Core.currentProfile.id, name, stored);
        }

        private static SaveBookFile Book => Plugin.Instance.Host.Book;

        private static SaveBookFile.FileData Data() => Book.Data(FilePath);

        private static void Write() => Book.Write(FilePath);

        /// <summary>
        /// The record for slot <paramref name="slot"/>, or null if there's
        /// none.
        /// </summary>
        private static Record Get(int slot)
        {
            var d = Data();
            if (d != null && d.Slots.TryGetValue(slot.ToString(), out var r))
            {
                return r;
            }

            return null;
        }

        private static Record GetOrAdd(int slot)
        {
            var d = Data();
            if (d == null)
            {
                return null;
            }

            if (!d.Slots.TryGetValue(slot.ToString(), out var r))
            {
                d.Slots[slot.ToString()] = r = new Record { Started = Now() };
            }

            return r;
        }

        private static string Now() =>
            DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// The name you gave the save in slot <paramref name="slot"/> ("" for
        /// none).
        /// </summary>
        public static string NameOf(int slot)
        {
            Record record = Get(slot);
            string name = null;
            if (record != null)
            {
                name = record.Name;
            }

            if (name == null)
            {
                name = "";
            }

            return name;
        }

        public static void SetName(int slot, string typed)
        {
            if (!SaveBookRules.Renames(NameOf(slot), typed))
            {
                return;
            }

            string name = SaveBookRules.SlotName(typed);

            var r = GetOrAdd(slot);
            if (r == null)
            {
                return;
            }

            r.Name = name;
            Write();
            Plugin.Log.LogInfo($"[saves] slot {slot} named \"{name}\"");
        }

        /// <summary>
        /// The names of those who've played slot <paramref name="slot"/> with
        /// you, most recent first. A save hosted before the mod kept these
        /// records has none: then the names come from the characters the host
        /// kept for the players who joined it
        /// (prof&lt;id&gt;/darkwoodmp_players).
        /// </summary>
        public static List<string> PlayedWith(int slot)
        {
            var r = Get(slot);
            if (r != null)
            {
                return r.Players.OrderByDescending(
                    p => p.Last, StringComparer.Ordinal).Select(p => p.Name)
                    .ToList();
            }

            var names = new List<string>();
            try
            {
                var sm = Singleton<SaveManager>.Instance;
                string dir;
                if (sm == null)
                {
                    dir = null;
                }
                else
                {
                    dir = DeleteRules.PlayersOf(sm.baseSaveDirectory, slot);
                }

                if (FilePath != null && dir != null && Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "*.json")
                        .OrderByDescending(File.GetLastWriteTimeUtc))
                    {
                        var c =
                            JsonConvert.DeserializeObject<CharacterRecord>(
                                File.ReadAllText(f));
                        if (c != null && !string.IsNullOrEmpty(c.Name)
                            && !names.Contains(c.Name))
                        {
                            names.Add(c.Name);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[saves] couldn't read slot {slot}'s players' characters: "
                        + $"{e.Message}");
            }

            return names;
        }

        public static void AddPlayer(int slot, string name, string key)
        {
            var r = GetOrAdd(slot);
            if (r == null)
            {
                return;
            }

            var joiner = new BookPlayer { Key = key, Name = name };
            var p = r.Players.FirstOrDefault(x => SaveBookRules.SamePlayer(
                new BookPlayer { Key = x.Key, Name = x.Name },
                joiner));

            if (p == null)
            {
                r.Players.Add(p = new Player { Key = key });
            }

            p.Name = name;
            p.Last = Now();
            Write();
            Plugin.Log.LogInfo($"[saves] slot {slot}: played with {name}");
        }

        /// <summary>
        /// Delete slot <paramref name="slot"/>'s players' characters;
        /// <paramref name="why"/> ends the log line.
        /// </summary>
        internal static void DeletePlayersDir(
            string baseDir, int slot, string why)
        {
            try
            {
                if (WorldFiles.DeletePlayersFolder(baseDir, slot))
                {
                    Plugin.Log.LogInfo(
                        $"[saves] slot {slot}'s players' characters deleted "
                            + $"{why}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[saves] couldn't delete slot {slot}'s players' "
                        + $"characters: {e.Message}");
            }
        }

        /// <summary>
        /// A new game in slot <paramref name="slot"/>, or the slot deleted: its
        /// record goes.
        /// </summary>
        public static void Forget(int slot, string why)
        {
            var d = Data();
            if (d == null || !d.Slots.Remove(slot.ToString()))
            {
                return;
            }

            Write();
            Plugin.Log.LogInfo($"[saves] slot {slot}'s record dropped ({why})");
        }

        /// <summary>
        /// A new game starts in a slot (UI.initNewGame, the profile isn't
        /// active yet): its record starts empty.
        /// </summary>
        internal static void OnNewGame()
        {
            var p = Core.currentProfile;
            bool active = false;
            if (p != null)
            {
                active = p.Active;
            }

            if (!RedirectRules.NewGameForgets(p != null, active))
            {
                return;
            }

            Forget(p.id, "a new game");
            // The characters the host kept for the players who joined the
            // slot's last world: the game writes the new world over the old
            // save and leaves them, so a joiner would get their old character
            // (dead, if it died for good). They go with the old world, as when
            // the slot is deleted.
            Plugin.Instance.Host.Kept.Forget("a new game");

            var sm = Singleton<SaveManager>.Instance;
            if (RedirectRules.NewGameDeletes(sm != null, HostCopy()))
            {
                DeletePlayersDir(sm.baseSaveDirectory, p.id, "(a new game)");
            }
        }

        /// <summary>
        /// The game deletes a slot's save (SaveManager.deleteCurrentProfile):
        /// its record goes too, and so do the characters the host kept for
        /// the players who joined it (prof&lt;id&gt;/darkwoodmp_players,
        /// World.Characters), which the game's deleteSave leaves behind:
        /// otherwise a new game in that slot would hand them their old
        /// characters.
        /// </summary>
        internal static void OnSlotDeleted(SaveManager saves)
        {
            var p = Core.currentProfile;
            bool? hostCopy = HostCopy();
            if (!RedirectRules.SlotDeleteForgets(p != null, hostCopy))
            {
                return;
            }

            Forget(p.id, "deleted");
            Plugin.Instance.Host.Kept.Forget("the slot deleted");

            DeletePlayersDir(saves.baseSaveDirectory, p.id, "with it");
        }
    }
}
