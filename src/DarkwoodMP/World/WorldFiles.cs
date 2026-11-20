using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A Darkwood world as the host shares it: its profile (the GameProfile, as JSON) and the profile folder's
    /// save files. Its hash is SHA-256 over the profile JSON's UTF-8 bytes, then, for each file in order,
    /// its name (str), its length (u32) and its bytes; both sides compute it the same way.
    /// </summary>
    public static class WorldFiles
    {
        /// <summary>The files that make up a profile's save, in the order they're sent. Only these are accepted.</summary>
        public static readonly string[] Names = { "sav.dat", "savs.dat", "savch.dat" };
        /// <summary>Largest world a client accepts (Darkwood's are about 12 MB).</summary>
        public const long MaxSize = 128L << 20;

        public sealed class File
        {
            public string Name;
            public byte[] Data;
        }

        public static byte[] Hash(string profileJson, IList<File> files)
        {
            using (var sha = SHA256.Create())
            {
                var pj = Encoding.UTF8.GetBytes(profileJson);
                sha.TransformBlock(pj, 0, pj.Length, null, 0);
                foreach (var f in files)
                {
                    var name = Encoding.UTF8.GetBytes(f.Name);
                    var head = new byte[2 + name.Length + 4];
                    BitConverter.GetBytes((ushort)name.Length).CopyTo(head, 0);
                    name.CopyTo(head, 2);
                    BitConverter.GetBytes((uint)f.Data.Length).CopyTo(head, 2 + name.Length);
                    sha.TransformBlock(head, 0, head.Length, null, 0);
                    sha.TransformBlock(f.Data, 0, f.Data.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return sha.Hash;
            }
        }

        /// <summary>Read the save files present in a profile folder, in Names order.</summary>
        public static List<File> Read(string profileDir)
        {
            var list = new List<File>();
            foreach (var n in Names)
            {
                string p = Path.Combine(profileDir, n);
                if (System.IO.File.Exists(p))
                {
                    list.Add(new File { Name = n, Data = System.IO.File.ReadAllBytes(p) });
                }
            }
            return list;
        }

        /// <summary>A signature of the files' sizes and times, to tell cheaply whether they changed.</summary>
        public static string Stamp(string profileDir)
        {
            var b = new StringBuilder();
            foreach (var n in Names)
            {
                var fi = new FileInfo(Path.Combine(profileDir, n));
                if (fi.Exists)
                {
                    b.Append(n).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
                }
            }
            return b.ToString();
        }

        /// <summary>Raw Deflate (RFC 1951), as SaveOffer's packing 1 sends the files.</summary>
        public static byte[] Deflate(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    z.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }

        /// <summary>Unpack what Deflate packed; it must come to exactly size bytes.</summary>
        public static byte[] Inflate(byte[] packed, int size)
        {
            var raw = new byte[size];
            using (var z = new DeflateStream(new MemoryStream(packed, false), CompressionMode.Decompress))
            {
                int at = 0, n;
                while (at < size && (n = z.Read(raw, at, size - at)) > 0)
                {
                    at += n;
                }
                if (at != size || z.Read(new byte[1], 0, 1) != 0)
                {
                    throw new InvalidDataException("the packed world unpacks to the wrong size");
                }
            }
            return raw;
        }

        public static string Hex(byte[] h) => BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        // ---- paths and deleting folders ------------------------------------------------------------------

        internal static readonly StringComparison PathCase = PathCaseOf(Environment.OSVersion.Platform);

        /// <summary>How paths compare on a platform: ignoring case on Windows, exactly elsewhere.</summary>
        private static StringComparison PathCaseOf(PlatformID platform)
        {
            if (platform == PlatformID.Win32NT)
            {
                return StringComparison.OrdinalIgnoreCase;
            }
            return StringComparison.Ordinal;
        }

        internal static string Full(string p) =>
            Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>The same folder, however the two were written (separators, a trailing slash; case on Windows).</summary>
        internal static bool SamePath(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            try
            {
                return string.Equals(Full(a), Full(b), PathCase);
            }
            catch
            {
                return a == b;
            }
        }

        /// <summary>Whether path is dir or inside it.</summary>
        internal static bool IsUnder(string path, string dir)
        {
            if (path == null || dir == null)
            {
                return false;
            }
            try
            {
                string p = Full(path), d = Full(dir);
                return string.Equals(p, d, PathCase) || p.StartsWith(d + Path.DirectorySeparatorChar, PathCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool StrictlyUnder(string full, string dir) => !SamePath(full, dir) && IsUnder(full, dir);

        /// <summary>
        /// Why a folder may never be deleted, or null if it may be considered at all: an empty or relative path,
        /// a filesystem root (Full trims "/" to "" and "C:\" to "C:", so a name-less or parent-less path is a root),
        /// or the user's home folder or one of its parents.
        /// </summary>
        private static string NeverDelete(string path, out string full)
        {
            full = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return "the path is empty";
            }
            if (!Path.IsPathRooted(path))
            {
                return "the path isn't absolute";
            }
            try
            {
                full = Full(path);
            }
            catch (Exception e)
            {
                return $"the path isn't valid ({e.Message})";
            }
            if (full.Length == 0 || Path.GetDirectoryName(full) == null || string.IsNullOrEmpty(Path.GetFileName(full)))
            {
                return "it's a filesystem root";
            }
            foreach (var home in new[] { Environment.GetEnvironmentVariable("HOME"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
            {
                if (!string.IsNullOrEmpty(home) && Path.IsPathRooted(home) && IsUnder(home, full))
                {
                    return "it's your home folder or holds it";
                }
            }
            return null;
        }

        /// <summary>
        /// The folders the mod deletes things in: its cache (BepInEx/cache/DarkwoodMP) and the folder standing in for
        /// the player's saves (SaveRedirect.Dir), unless that is itself a root or the home folder.
        /// </summary>
        private static IEnumerable<string> DeleteRoots()
        {
            yield return Path.Combine(BepInEx.Paths.CachePath, "DarkwoodMP");
            string redirect = SaveRedirect.Dir;
            if (redirect != null && NeverDelete(redirect, out _) == null)
            {
                yield return redirect;
            }
        }

        /// <summary>
        /// Deletes a folder and everything in it, only if it's strictly inside the mod's cache folder or the active
        /// save-redirect folder; anything else is refused with a warning and left alone (false). A symbolic link or
        /// other reparse point, the folder itself or anything in it, is deleted as a link: what it points at is never
        /// touched. Nothing there is true. Errors while deleting are thrown, as Directory.Delete throws them.
        /// </summary>
        public static bool DeleteTree(string path)
        {
            if (!MayDelete(path, out string full))
            {
                return false;
            }
            DeleteEntry(full);
            return true;
        }

        /// <summary>Whether DeleteTree would delete path; logs a warning when not.</summary>
        public static bool MayDelete(string path) => MayDelete(path, out _);

        private static bool MayDelete(string path, out string full)
        {
            string why = NeverDelete(path, out full);
            string f = full;
            if (why == null && !DeleteRoots().Any(root => StrictlyUnder(f, root)))
            {
                why = "it isn't inside the mod's cache or save-redirect folder";
            }
            if (why == null)
            {
                return true;
            }
            string shownPath = path;
            if (shownPath == null)
            {
                shownPath = "(null)";
            }
            Plugin.Log.LogWarning($"Not deleting {shownPath}: {why}");
            return false;
        }

        /// <summary>
        /// Deletes the characters the host keeps for slot <paramref name="slot"/>'s joiners, base/prof&lt;slot&gt;/darkwoodmp_players,
        /// which lives in the player's own save folder (so DeleteTree's roots don't cover it). Only that exact folder:
        /// false, with a warning, for a bad base folder or slot. True if it was there and is deleted.
        /// </summary>
        public static bool DeletePlayersFolder(string baseDir, int slot)
        {
            string why;
            if (slot < 1 || slot > 99)
            {
                why = $"slot {slot} isn't a save slot";
            }
            else
            {
                why = NeverDelete(baseDir, out _);
            }
            string dir = null, full = null;
            if (why == null)
            {
                dir = Path.Combine(Path.Combine(baseDir, "prof" + slot), "darkwoodmp_players");
                full = Full(dir);
                string expected = Full(baseDir) + Path.DirectorySeparatorChar + "prof" + slot + Path.DirectorySeparatorChar + "darkwoodmp_players";
                if (!string.Equals(full, expected, PathCase))
                {
                    why = "it isn't prof<N>/darkwoodmp_players in the save folder";
                }
            }
            if (why != null)
            {
                string shownDir = dir;
                if (shownDir == null)
                {
                    shownDir = baseDir;
                }
                if (shownDir == null)
                {
                    shownDir = "(null)";
                }
                Plugin.Log.LogWarning($"Not deleting {shownDir}: {why}");
                return false;
            }
            if (!Exists(full))
            {
                return false;
            }
            DeleteEntry(full);
            return true;
        }

        private static bool IsLink(string p)
        {
            try
            {
                return (System.IO.File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool Exists(string p) => Directory.Exists(p) || System.IO.File.Exists(p) || IsLink(p);

        /// <summary>Deletes a file, a link (as a link) or a folder with what's in it, never following a link.</summary>
        private static void DeleteEntry(string p)
        {
            if (IsLink(p))
            {
                // A link to a folder: Windows removes a junction or folder link with RemoveDirectory, Unix a symlink with
                // unlink (rmdir refuses one). Neither goes into the folder it points at.
                try
                {
                    Directory.Delete(p, false);
                }
                catch (Exception)
                {
                    System.IO.File.Delete(p);
                }
                return;
            }
            if (!Directory.Exists(p))
            {
                if (System.IO.File.Exists(p))
                {
                    System.IO.File.SetAttributes(p, FileAttributes.Normal);
                    System.IO.File.Delete(p);
                }
                return;
            }
            foreach (var e in Directory.GetFileSystemEntries(p))
            {
                DeleteEntry(e);
            }
            Directory.Delete(p, false);
        }
    }
}
