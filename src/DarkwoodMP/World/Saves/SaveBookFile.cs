using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The save book's file (SaveBook.FilePath) as last read, read again
    /// when it changed or went away underneath. It lives as long as the game
    /// (ModHost.Book): the Start Game window reads it at the main menu.
    /// </summary>
    internal sealed class SaveBookFile
    {
        public sealed class FileData
        {
            public Dictionary<string, SaveBook.Record> Slots =
                new Dictionary<string, SaveBook.Record>();
        }

        private FileData _data;
        private string _loadedFrom;
        private DateTime _stamp;

        /// <summary>The book in path, or null with no path.</summary>
        public FileData Data(string path)
        {
            if (path == null)
            {
                return null;
            }

            // Read again when the file changed or went away underneath (a
            // test's fresh copy of the saves).
            DateTime stamp;
            if (File.Exists(path))
            {
                stamp = File.GetLastWriteTimeUtc(path);
            }
            else
            {
                stamp = DateTime.MinValue;
            }

            if (_data != null && _loadedFrom == path && _stamp == stamp)
            {
                return _data;
            }

            _loadedFrom = path;
            _stamp = stamp;
            _data = new FileData();
            try
            {
                if (File.Exists(path))
                {
                    FileData data = JsonConvert.DeserializeObject<FileData>(
                        File.ReadAllText(path));
                    if (data == null)
                    {
                        data = new FileData();
                    }

                    _data = data;
                }

                if (_data.Slots == null)
                {
                    _data.Slots = new Dictionary<string, SaveBook.Record>();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[saves] couldn't read {path}: {e.Message}");
            }

            return _data;
        }

        /// <summary>
        /// Writes the book read from path; nothing when it was read from
        /// another or none.
        /// </summary>
        public void Write(string path)
        {
            if (path == null || _data == null || _loadedFrom != path)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(
                    tmp,
                    JsonConvert.SerializeObject(_data, Formatting.Indented));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tmp, path);
                _stamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[saves] couldn't write {path}: {e.Message}");
            }
        }
    }
}
