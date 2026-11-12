using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
                if (System.IO.File.Exists(p)) list.Add(new File { Name = n, Data = System.IO.File.ReadAllBytes(p) });
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
                if (fi.Exists) b.Append(n).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
            }
            return b.ToString();
        }

        /// <summary>Raw Deflate (RFC 1951), as SaveOffer's packing 1 sends the files.</summary>
        public static byte[] Deflate(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
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
                while (at < size && (n = z.Read(raw, at, size - at)) > 0) at += n;
                if (at != size || z.Read(new byte[1], 0, 1) != 0) throw new InvalidDataException("the packed world unpacks to the wrong size");
            }
            return raw;
        }

        public static string Hex(byte[] h) => BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
