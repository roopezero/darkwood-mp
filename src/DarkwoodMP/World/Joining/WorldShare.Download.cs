using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World.Joining;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Client: the world downloaded from the host and cached.
    /// </summary>
    public sealed partial class WorldShare
    {
        // =====================================================================
        // Client
        // =====================================================================

        private sealed class Download
        {
            public byte[] Hash;
            public string ProfileJson;
            public GameProfile Profile;
            public List<(string Name, int Size)> Files;
            public byte Packing;
            public int RawSize;
            public byte[] Blob;      // the bytes as sent (packed)
            public bool[] Have;      // which pieces of it have come
            public uint NextOffset;
            public long Received;
            public float LastPieceAt;
            // when the offer came, for the transfer time in the log
            public float StartedAt;
            public int Stalls;
        }

        private Download _download;
        private bool _unpacking;
        // the world we downloaded or found, sent in the last Hello
        private byte[] _readyHash;

        // A download that gets no piece for StallSecs asks again for every
        // piece it's missing; after MaxStalls of those in a row it gives up, so
        // the loading screen can't wait for ever.
        private static readonly float StallSecs = OfferRules.StallSecs.Value;
        private const int MaxStalls = OfferRules.MaxStalls;

        /// <summary>
        /// For the panel: (bytes got, bytes total) while downloading the host's
        /// world.
        /// </summary>
        public (long Got, long Total)? Progress
        {
            get
            {
                if (_download == null)
                {
                    return ((long, long)?)null;
                }

                return (_download.Received, _download.Blob.Length);
            }
        }

        /// <summary>
        /// The download's size unpacked, for the loading screen (0: not packed,
        /// or no download).
        /// </summary>
        public long UnpackedSize
        {
            get
            {
                if (_download != null && _download.Packing != 0)
                {
                    return _download.RawSize;
                }

                return 0;
            }
        }

        public string LoadStatus { get; private set; }

        private byte[] ClientHelloHash()
        {
            // The newest world we've got that's still as it came (playing it
            // changes it, as the game saves into it).
            _readyHash = null;
            try
            {
                foreach (var dir in new DirectoryInfo(_cacheDir)
                    .GetDirectories().OrderByDescending(d =>
                    d.LastWriteTimeUtc))
                {
                    var h = HashOfCached(dir.FullName);
                    if (h != null && WorldFiles.Hex(h) == dir.Name)
                    {
                        _readyHash = h;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't check cached worlds: {e.Message}");
            }

            return _readyHash;
        }

        // Hashing a cached world reads all of it (tens of MB), and Hello (sent
        // every 3 s while the host keeps us waiting) asks for it: so each
        // folder's hash is kept until a file in it changes.
        private readonly Dictionary<string, (string Stamp, byte[] Hash)>
            _hashCache = new Dictionary<string, (string, byte[])>();

        private byte[] HashOfCached(string worldDir)
        {
            string stamp;
            try
            {
                long n = 0, bytes = 0, newest = 0;
                foreach (var f in new DirectoryInfo(worldDir).GetFiles(
                    "*", SearchOption.AllDirectories))
                {
                    n++;
                    bytes += f.Length;
                    newest = Math.Max(newest, f.LastWriteTimeUtc.Ticks);
                }

                stamp = $"{n}/{bytes}/{newest}";
            }
            catch
            {
                return HashOfCachedNow(worldDir);
            }

            if (_hashCache.TryGetValue(worldDir, out var c) && c.Stamp == stamp)
            {
                return c.Hash;
            }

            var h = HashOfCachedNow(worldDir);
            _hashCache[worldDir] = (stamp, h);
            return h;
        }

        private static byte[] HashOfCachedNow(string worldDir)
        {
            string pj = Path.Combine(worldDir, "profile.json");
            if (!File.Exists(pj))
            {
                return null;
            }

            string json = File.ReadAllText(pj);
            var profile = JsonConvert.DeserializeObject<GameProfile>(json);
            if (profile == null)
            {
                return null;
            }

            return WorldFiles.Hash(
                json, WorldFiles.Read(ProfDir(worldDir, profile.id)));
        }

        private void OnSaveOffer(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            var offer = SaveOffer.Read(r);
            var d = new Download { Hash = offer.Hash };
            // The same world again (an answer to a Hello sent before the first
            // offer came): go on with this download.
            if ((_download != null
                    && WorldFiles.Equal(_download.Hash, d.Hash)) || _unpacking)
            {
                return;
            }

            uint size = offer.Size;
            d.ProfileJson = offer.ProfileJson;
            d.Files = new List<(string, int)>();
            foreach (var f in offer.Files)
            {
                d.Files.Add((f.Name, (int)f.Size));
            }

            d.Packing = offer.Packing;
            uint raw = offer.Raw;
            string profileError = null;
            try
            {
                d.Profile =
                    JsonConvert.DeserializeObject<GameProfile>(d.ProfileJson);
            }
            catch (Exception e)
            {
                profileError = e.Message;
            }

            var verdict =
                OfferRules.Check(FactsOf(offer, d.Profile, profileError));
            if (verdict.Refused)
            {
                Fail(Refusals.Text(verdict));
                return;
            }

            d.RawSize = (int)raw;
            d.Blob = new byte[size];
            d.Have = new bool[OfferRules.Pieces(size)];
            d.LastPieceAt = d.StartedAt = Time.realtimeSinceStartup;
            _download = d;
            LoadStatus = null;
            string text = "";
            if (d.Packing == 1)
            {
                text = $", {raw / 1024} KiB unpacked";
            }

            Plugin.Log.LogInfo(
                $"Downloading the host's world ({size / 1024} KiB{text}, day "
                    + $"{d.Profile.day})");
            Plugin.Instance.Host.Join.Begin();
            for (int i = 0; i < PiecesInFlight; i++)
            {
                RequestNext();
            }
        }

        private static OfferFacts FactsOf(
            in SaveOfferBody offer, GameProfile profile, string error)
        {
            int id = 0;
            if (profile != null)
            {
                id = profile.id;
            }

            return new OfferFacts
            {
                Files = offer.Files,
                Packing = offer.Packing,
                Raw = offer.Raw,
                Size = offer.Size,
                ProfileError = error,
                Profile = profile != null,
                ProfileId = id,
            };
        }

        private void RequestNext()
        {
            var d = _download;
            if (d == null || d.NextOffset >= d.Blob.Length)
            {
                return;
            }

            RequestPiece(d, d.NextOffset);
            d.NextOffset += PieceSize;
        }

        private void RequestPiece(Download d, uint offset)
        {
            var w = _session.Begin(MsgType.NeedSave);
            NeedSave.Write(
                w,
                new WorldPiece
                {
                    Hash = d.Hash,
                    Offset = offset,
                    Length = (uint)PieceSize,
                });

            _session.SendToHost(w, Channel.Control, reliable: true);
        }

        private void OnSaveData(ulong from, PacketReader r)
        {
            var d = _download;
            if (from != _session.HostId || d == null)
            {
                return;
            }

            var head = SaveData.ReadHead(r);
            var hash = head.Hash;
            uint offset = head.Offset;
            int n = (int)head.Length;
            if (!WorldFiles.Equal(hash, d.Hash))
            {
                return; // a piece of an earlier offer, still on its way
            }

            long piece = offset / PieceSize;
            var check = OfferRules.CheckPiece(
                new PieceFacts
                {
                    Offset = offset,
                    Length = n,
                    Total = d.Blob.Length,
                });

            if (check.Refused)
            {
                Fail(Refusals.Text(check));
                return;
            }

            d.LastPieceAt = Time.realtimeSinceStartup;
            d.Stalls = 0;
            if (d.Have[piece])
            {
                return; // asked for twice (after a stall), came twice
            }

            if (!SaveData.ReadInto(r, d.Blob, head))
            {
                Fail(Refusals.Text(Verdict.Refuse(Refusal.ShortPiece)));
                return;
            }

            d.Have[piece] = true;
            d.Received += n;
            if (d.Received < d.Blob.Length)
            {
                RequestNext();
                return;
            }

            float secs = Time.realtimeSinceStartup - d.StartedAt;
            Plugin.Log.LogInfo(
                $"[join] the world's {d.Blob.Length / 1024} KiB came in "
                    + $"{secs * 1000f:0} ms "
                    + $"({d.Blob.Length / 1024f / Mathf.Max(secs, 0.001f):0} "
                    + $"KiB/s, {d.Have.Length} pieces)");
            _runner.StartCoroutine(Finish(d));
        }

        /// <summary>
        /// Client, every frame: a download that has stopped coming asks again,
        /// then gives up.
        /// </summary>
        private void UpdateDownload()
        {
            var d = _download;
            if (d == null
                || !OfferRules.Stalled(
                    new Seconds(d.LastPieceAt),
                    new Seconds(Time.realtimeSinceStartup)))
            {
                return;
            }

            d.LastPieceAt = Time.realtimeSinceStartup;
            var verdict = OfferRules.AfterStall(
                ++d.Stalls, d.Received, d.Blob.Length);
            if (verdict.Refused)
            {
                Fail(Refusals.Text(verdict));
                return;
            }

            int asked = 0;
            for (int i = 0; i < d.Have.Length
                && (uint)i * PieceSize < d.NextOffset; i++)
            {
                if (!d.Have[i])
                {
                    RequestPiece(d, (uint)(i * PieceSize));
                    asked++;
                }
            }

            Plugin.Log.LogWarning(
                $"No piece of the host's world for {StallSecs:0} s: asked "
                    + $"again for {asked} ({d.Stalls}/{MaxStalls})");
        }

        /// <summary>
        /// The whole world has come: unpack it, check its hash and store it on
        /// a worker thread (tens of MB), then ask to join with it.
        /// </summary>
        private IEnumerator Finish(Download d)
        {
            _download = null;
            _unpacking = true;
            LoadStatus = "Unpacking and checking the host's world...";
            string hex = WorldFiles.Hex(d.Hash);
            string dir = Path.Combine(_cacheDir, hex);
            long took = 0;
            var job = System.Threading.Tasks.Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                byte[] raw;
                if (d.Packing == 1)
                {
                    raw = WorldFiles.Inflate(d.Blob, d.RawSize);
                }
                else
                {
                    raw = d.Blob;
                }

                var files = new List<WorldFiles.File>();
                int at = 0;
                foreach (var (name, size) in d.Files)
                {
                    var data = new byte[size];
                    Buffer.BlockCopy(raw, at, data, 0, size);
                    at += size;
                    files.Add(new WorldFiles.File { Name = name, Data = data });
                }

                if (!WorldFiles.Equal(
                    WorldFiles.Hash(d.ProfileJson, files), d.Hash))
                {
                    return "the world didn't arrive intact (hash mismatch)";
                }

                RemoveDir(dir);
                string profDir = ProfDir(dir, d.Profile.id);
                Directory.CreateDirectory(profDir);
                foreach (var f in files)
                {
                    File.WriteAllBytes(Path.Combine(profDir, f.Name), f.Data);
                }

                File.WriteAllText(
                    Path.Combine(dir, "profile.json"), d.ProfileJson);
                Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow);
                took = sw.ElapsedMilliseconds;
                return null;
            });

            while (!job.IsCompleted)
            {
                yield return null;
            }

            _unpacking = false;
            string error;
            if (job.IsFaulted)
            {
                AggregateException exception = job.Exception;
                string message = null;
                if (exception != null)
                {
                    message = exception.GetBaseException().Message;
                }

                error = $"couldn't store the world: {message}";
            }
            else
            {
                error = job.Result;
            }

            if (!_session.Active || _session.IsHost)
            {
                yield break; // stopped joining meanwhile
            }

            if (error != null)
            {
                Fail(error);
                yield break;
            }

            Plugin.Log.LogInfo(
                $"[join] the world was unpacked, checked and stored in {took} "
                    + "ms on a worker thread");
            try
            {
                PruneCache(dir);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't tidy the world cache: {e.Message}");
            }

            Plugin.Log.LogInfo(
                $"Got the host's world {hex.Substring(0, 12)}; asking to join "
                    + "with it");
            LoadStatus = null;
            _readyHash = d.Hash;
            // send exactly this one in the next Hello
            _session.ClientWorldHash = () => _readyHash;
            _session.SendHello();
            _session.ClientWorldHash = ClientHelloHash;
        }

        private void PruneCache(string keep)
        {
            foreach (var old in new DirectoryInfo(_cacheDir).GetDirectories()
                .OrderByDescending(x => x.LastWriteTimeUtc).Skip(KeepWorlds))
            {
                if (WorldFiles.SamePath(old.FullName, keep)
                    || WorldFiles.SamePath(
                        old.FullName, Plugin.Instance.Host.SaveFolders.Dir))
                {
                    continue; // never the world being played
                }

                try
                {
                    WorldFiles.DeleteTree(old.FullName);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Deletes a folder of ours, to be made again at once. On Windows, a
        /// file in it that another program has open for a moment (a virus
        /// scanner, the search indexer) makes Directory.Delete fail, or leaves
        /// the folder waiting to be deleted so that making it again fails. So
        /// it's renamed out of the way first; if it can't be deleted then,
        /// what's left is deleted the next time. Every delete goes through
        /// WorldFiles.DeleteTree, which refuses (with a warning, deleting
        /// nothing) anything outside the mod's cache and save-redirect folders.
        /// </summary>
        internal static void RemoveDir(string dir)
        {
            if (!WorldFiles.MayDelete(dir))
            {
                return; // refused, with a warning
            }

            dir = WorldFiles.Full(dir);
            string parent = Path.GetDirectoryName(dir),
                aside = Path.GetFileName(dir) + ".old-";
            if (Directory.Exists(parent))
            {
                foreach (var old in Directory.GetDirectories(
                    parent, aside + "*"))
                {
                    try
                    {
                        WorldFiles.DeleteTree(old);
                    }
                    catch
                    {
                    }
                }
            }

            if (!Directory.Exists(dir))
            {
                return;
            }

            string moved = Path.Combine(parent, aside + DateTime.UtcNow.Ticks);
            try
            {
                Directory.Move(dir, moved);
            }
            catch (Exception) // can't be moved: delete it where it is
            {
                WorldFiles.DeleteTree(dir);
                return;
            }

            try
            {
                WorldFiles.DeleteTree(moved);
            }
            catch
            {
            }
        }

        /// <summary>
        /// We're playing a copy of another host's world (from the cache), e.g.
        /// after leaving their game. Hosting it would hand joiners that stale
        /// copy as if it were a world of its own, so the panel won't host it.
        /// </summary>
        public bool PlayingHostCopy => Plugin.Instance.Host.SaveFolders.Active
            && WorldFiles.IsUnder(
                Plugin.Instance.Host.SaveFolders.Dir, _cacheDir);

        private void Fail(string why)
        {
            _download = null;
            LoadStatus = $"Couldn't get the host's world: {why}";
            Plugin.Log.LogError(LoadStatus);
            INetBackend backend = _session.Backend;
            if (backend != null)
            {
                backend.Leave();
            }
        }
    }
}
