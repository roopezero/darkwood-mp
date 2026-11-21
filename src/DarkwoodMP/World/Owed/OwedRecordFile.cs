using System;
using System.IO;
using Newtonsoft.Json;

namespace DarkwoodMP.World.Owed
{
    /// <summary>
    /// Host: who had their own part of which once-only trigger, and what the
    /// host's own player is owed (OwedParts), kept on ModHost for as long as
    /// the game runs: it outlasts a session and a world reload, and is written
    /// beside the save whenever the game saves (prof&lt;id&gt;/
    /// darkwoodmp_later.json), with or without a session.
    /// </summary>
    internal sealed class OwedRecordFile
    {
        private OwedRecord _data;
        // the profile folder _data.Had belongs to
        private string _dir;
        // the loaded world _data.HostOwed belongs to
        private int _owedScene;

        private static string ProfileDir()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return null;
            }

            return Path.Combine(
                sm.baseSaveDirectory, "prof" + Core.currentProfile.id);
        }

        private static OwedRecord Read(string dir)
        {
            try
            {
                string path = null;
                if (dir != null)
                {
                    path = Path.Combine(dir, OwedRules.FileName);
                }

                if (path != null && File.Exists(path))
                {
                    OwedRecord d = JsonConvert.DeserializeObject<OwedRecord>(
                        File.ReadAllText(path));
                    if (d == null)
                    {
                        d = new OwedRecord();
                    }

                    d.Repair();
                    return d;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Couldn't read who is owed their part of fired events in "
                        + $"{dir}: {e.Message}");
            }

            return new OwedRecord();
        }

        /// <summary>
        /// The record for this world, read from beside its save the first
        /// time, and again on a new world.
        /// </summary>
        public OwedRecord Data()
        {
            string dir = ProfileDir();
            int scene =
                UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().handle;
            RecordRead read = OwedRules.Reads(new RecordReadFacts
            {
                Loaded = _data != null,
                Profile = dir,
                RecordProfile = _dir,
                Scene = scene,
                OwedScene = _owedScene,
            });

            if (read == RecordRead.Whole)
            {
                _data = Read(dir);
                _dir = dir;
                _owedScene = scene;
            }
            else if (read == RecordRead.HostOwed)
            {
                // The world was loaded again (a reload, a new chapter): our
                // player is where the save has them, and so is what they're
                // owed. What the joiners had stays: their characters are kept
                // as they last sent them.
                _data.HostOwed = Read(dir).HostOwed;
                _owedScene = scene;
            }

            return _data;
        }

        /// <summary>
        /// Write the record beside the save (the game saved).
        /// </summary>
        public void Saved()
        {
            string dir = ProfileDir();
            bool empty = false;
            if (_data != null)
            {
                empty = _data.Empty;
            }

            bool writes = OwedRules.Writes(new RecordSaveFacts
            {
                Snapshotting = WorldShare.Snapshotting,
                Client = Combat.NetEnemies.ClientMode,
                Loaded = _data != null,
                Profile = dir,
                RecordProfile = _dir,
                Empty = empty,
            });

            if (!writes)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, OwedRules.FileName),
                    JsonConvert.SerializeObject(
                        new { _data.Had, _data.HostOwed }));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Couldn't write who is owed their part of fired events: "
                        + $"{e.Message}");
            }
        }

        /// <summary>
        /// For the tests: the host's game saves and starts again (the record is
        /// written, then read back).
        /// </summary>
        public void TestReload()
        {
            if (_data == null)
            {
                Data();
            }

            Saved();
            _data = null;
        }
    }
}
