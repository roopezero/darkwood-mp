using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DarkwoodMP.Combat;
using Newtonsoft.Json;

namespace DarkwoodMP.World.KeptCharacters
{
    /// <summary>
    /// Host: the characters of the players who joined this world, kept by
    /// key beside its save (prof&lt;id&gt;/darkwoodmp_players/&lt;hash of
    /// key&gt;.json) and written whenever its game saves. They live as long
    /// as the game (ModHost.Kept): SaveManager.Save writes them also with no
    /// session, and a new game or a deleted slot forgets them.
    /// </summary>
    internal sealed class CharacterRecords
    {
        private readonly Dictionary<string, CharacterRecord> _records =
            new Dictionary<string, CharacterRecord>();
        private string _recordsDir; // which world _records belong to

        internal static string KeyHash(string key)
        {
            using (var sha = SHA256.Create())
            {
                string hashed = key;
                if (hashed == null)
                {
                    hashed = "";
                }

                return WorldFiles.Hex(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(hashed))).Substring(
                        0, 32);
            }
        }

        private string RecordsDir()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return null;
            }

            return Path.Combine(
                Path.Combine(
                    sm.baseSaveDirectory, "prof" + Core.currentProfile.id),
                "darkwoodmp_players");
        }

        /// <summary>
        /// A new world in a save folder that had one (a new game in the slot, a
        /// deleted slot, a reset test world):
        /// what was read from that folder is dropped, so it's read again.
        /// Records() only notices a different folder, so without this a new
        /// world in the same one would get the old world's characters, one that
        /// died for good too.
        /// </summary>
        public void Forget(string why)
        {
            if (_recordsDir == null && _records.Count == 0)
            {
                return;
            }

            if (_records.Count > 0)
            {
                Plugin.Log.LogInfo(
                    "Players' characters of the last world in this folder "
                        + $"forgotten ({why})");
            }

            _records.Clear();
            _recordsDir = null;
        }

        /// <summary>
        /// The characters of this world: loaded from beside its save the first
        /// time they're needed.
        /// </summary>
        public Dictionary<string, CharacterRecord> Records()
        {
            string dir = RecordsDir();
            if (!CharacterRules.Rereads(dir, _recordsDir))
            {
                return _records;
            }

            _records.Clear();
            _recordsDir = dir;
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        var rec =
                            JsonConvert.DeserializeObject<CharacterRecord>(
                                File.ReadAllText(f));
                        if (rec != null)
                        {
                            _records[Path.GetFileNameWithoutExtension(f)] = rec;
                        }
                    }
                }

                if (_records.Count > 0)
                {
                    Plugin.Log.LogInfo(
                        $"This world keeps {_records.Count} other players' "
                            + "characters");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't read players' characters in {dir}: {e.Message}");
            }

            return _records;
        }

        /// <summary>
        /// Host: the game saved; write the characters beside it.
        /// </summary>
        public void HostSaved()
        {
            if (!CharacterRules.Writes(
                WorldShare.Snapshotting, NetEnemies.ClientMode))
            {
                return; // a snapshot for a joiner, or a client's copy
            }

            var recs = Records();
            if (!CharacterRules.HasToWrite(recs.Count, _recordsDir != null))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_recordsDir);
                foreach (var kv in recs)
                {
                    File.WriteAllText(
                        Path.Combine(_recordsDir, kv.Key + ".json"),
                        JsonConvert.SerializeObject(kv.Value));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't write players' characters: {e.Message}");
            }
        }
    }
}
