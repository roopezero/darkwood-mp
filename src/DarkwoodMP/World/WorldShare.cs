using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Shares the host's world with the players who join, as goasis hands its pack to them.
    ///
    /// Host: a client whose Hello carries another world hash than the host's gets SaveOffer: the hash, the
    /// profile and the files' names and sizes. The offered snapshot is kept for that client, so the host saving
    /// meanwhile doesn't change what it's downloading. The client asks for it a piece at a time (NeedSave) and
    /// gets each as SaveData.
    ///
    /// Client: the world goes into BepInEx/cache/DarkwoodMP/worlds/&lt;hash&gt;/, laid out as 1_4Save is
    /// (prof&lt;id&gt;/sav.dat ...), plus profile.json. Once it has all come and its hash checks out, the client
    /// sends Hello again with that hash, is let in, and loads it with saves redirected there (SaveRedirect).
    /// The world the client last downloaded is offered in Hello while it's unchanged, so rejoining a host that
    /// hasn't saved since needn't download it again.
    /// </summary>
    public sealed class WorldShare
    {
        public static WorldShare Instance { get; private set; }
        public const int PieceSize = 256 << 10;
        private const int PiecesInFlight = 4;
        private const int KeepWorlds = 3;

        private readonly Session _session;
        private readonly MonoBehaviour _runner;
        private readonly string _cacheDir;

        public WorldShare(Session session, MonoBehaviour runner, string cacheRoot)
        {
            Instance = this;
            _session = session;
            _runner = runner;
            _cacheDir = Path.Combine(cacheRoot, "DarkwoodMP", "worlds");
            Directory.CreateDirectory(_cacheDir);

            SharedIds.Init(session);
            session.BeforeWelcome = HostBeforeWelcome;
            session.ClientWorldHash = ClientHelloHash;
            session.Register(MsgType.SaveOffer, OnSaveOffer);
            session.Register(MsgType.NeedSave, OnNeedSave);
            session.Register(MsgType.SaveData, OnSaveData);
            session.OnWelcomed += OnWelcomed;
            session.OnPeerDisconnected += id => { _offered.Remove(id); _waiting.Remove(id); _staleTold.Remove(id); if (_watchPeer == id) EndWatch("gone"); };
            session.OnSessionEnded += () => { _offered.Clear(); _waiting.Clear(); _staleTold.Clear(); _download = null; _hostWorldKey = 0; EndWatch("the session ended"); };
            session.OnJoinTimedOut += why => { _download = null; LoadStatus = why; };
            session.Register(MsgType.WorldReload, (from, r) =>
            {
                if (from != _session.HostId || _session.IsHost) return;
                Plugin.Notice("The host's world changed: loading it again");
                _session.Backend?.Rejoin();
            });
            session.Register(MsgType.JoinWait, (from, r) =>
            {
                if (from != _session.HostId || _session.IsHost || _session.Connected) return;
                string why = r.ReadStr();
                if (LoadStatus != why) Plugin.Log.LogInfo($"Waiting to join: {why}");
                LoadStatus = why;
                if (_download != null) { Plugin.Log.LogInfo("The host no longer offers the world we were downloading"); _download = null; }
                if (why == Preparing) JoinScreen.Begin(); // it's on its way: the loading screen says so
                _runner.StartCoroutine(HelloAgain());
            });
            session.Register(MsgType.HostAway, (from, r) =>
            {
                if (from != _session.HostId || _session.IsHost) return;
                _session.Backend?.Leave();
                LeaveHostWorld("The host went back to the main menu and stopped the server.");
            });
            session.OnHostLost += why => LeaveHostWorld($"Lost the host: {why}");
            session.OnRefused += why => { if (JoinScreen.Shown) LoadStatus = $"The host refused you: {why}"; };
        }

        // ---- the host's world changing under the players ------------------------------------

        private int _hostWorldKey;

        /// <summary>
        /// Host, going back to the main menu (Core.returnToMainMenu: the pause menu's Main menu, the permadeath
        /// screen, the credits): the world the players are in is gone, so the server stops and they're sent to
        /// their own main menus.
        /// </summary>
        public void HostToMainMenu()
        {
            if (!_session.IsHost) return;
            Plugin.Log.LogInfo("Back to the main menu: the server stops");
            StopServer(_session.Begin(MsgType.HostAway));
        }

        /// <summary>Host: send the players a last message (HostAway, GameEnd) and stop the server once they've gone.</summary>
        public void StopServer(PacketWriter farewell)
        {
            if (!_session.IsHost) return;
            if (_session.Peers.Count == 0) { _session.Backend.Leave(); return; }
            _session.Broadcast(farewell, Channel.Control, reliable: true);
            _runner.StartCoroutine(StopServerSoon(_session.Backend));
        }

        /// <summary>Client: what the co-op panel says once we're back at the main menu.</summary>
        public void SetLoadStatus(string why) => LoadStatus = why;

        /// <summary>
        /// The players leave as HostAway reaches them. Closing their connections from here first could lose it: a
        /// socket closed with their updates still unread is reset, not closed.
        /// </summary>
        private IEnumerator StopServerSoon(INetBackend backend)
        {
            float until = Time.realtimeSinceStartup + 1f;
            while (_session.Peers.Count > 0 && Time.realtimeSinceStartup < until) yield return null;
            if (_session.Backend == backend && backend.InSession && backend.IsHost) backend.Leave();
        }

        /// <summary>Client: the host is gone. Leave its world (our copy of it) for the main menu.</summary>
        private void LeaveHostWorld(string why)
        {
            LoadStatus = why;
            Plugin.Log.LogInfo(why);
            if (SaveRedirect.Active) _runner.StartCoroutine(ToMainMenu());
        }

        private IEnumerator ToMainMenu()
        {
            // A world still loading is let finish first: leaving in the middle of it isn't something the game does.
            float until = Time.realtimeSinceStartup + 120f;
            while (!InWorld && !MainMenuReady() && Time.realtimeSinceStartup < until) yield return null;
            if (_session.Active || !InWorld || !SaveRedirect.Active) yield break; // joined again meanwhile, or already out
            if (Core.mainMenu) Singleton<MainMenu>.Instance.close(); // the pause menu
            Core.returnToMainMenu();
            yield return null;
            while (!MainMenuReady()) yield return null;
            Plugin.Log.LogInfo("Back at the main menu");
            Plugin.Instance.OpenPanel(); // it says why (LoadStatus)
        }

        /// <summary>
        /// Host: when its world changes (a new chapter, a save loaded again), the players in it load it again.
        /// Going back to the main menu stops the server instead (HostToMainMenu).
        /// </summary>
        public void Update()
        {
            if (_session.IsHost) UpdateWatch();
            else UpdateDownload();
            if (!_session.IsHost || _session.Peers.Count == 0) { if (!_session.IsHost) _hostWorldKey = 0; return; }
            if (!WorldReady) return;
            int key = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (_hostWorldKey != 0 && key != _hostWorldKey)
            {
                Plugin.Log.LogInfo("Your world changed: the players in it load it again");
                SharedIds.ResetHost();
                _session.Broadcast(_session.Begin(MsgType.WorldReload), Channel.Control, reliable: true);
            }
            _hostWorldKey = key;
        }

        /// <summary>Client: ask to join again after a JoinWait.</summary>
        private System.Collections.IEnumerator HelloAgain()
        {
            yield return new WaitForSecondsRealtime(3f);
            // Not while a world is coming in (the offer came meanwhile): that Hello would only be offered it again.
            if (_session.Active && !_session.IsHost && !_session.Connected && _download == null && !_unpacking) _session.SendHello();
        }

        /// <summary>
        /// True while the game has a world loaded and running (not the main menu or a loading screen). The pause
        /// menu in a world counts as in it: Core.mainMenu is set for it too, but the world is still there.
        /// </summary>
        public static bool InWorld => (!Core.mainMenu || PauseMenuInWorld) && Core.currentProfile != null && Player.Instance != null;

        /// <summary>The menu is open over a world (the pause menu), not the main menu's own scene.</summary>
        public static bool PauseMenuInWorld => Core.mainMenu && Singleton<WorldGenerator>.Instance != null && Core.worldGenFinished();

        /// <summary>
        /// True while the game is loading a world, by the game's own test for it (EventTriggers doesn't fire while
        /// this holds): the save loader (SaveManager.Load) makes every saved object again with Core.AddPrefab while
        /// Core.loadingGame is set, and keeps dontFireTriggers set until 2 s after it ends. Nothing made or changed
        /// then is a player's doing, so none of it is shared (WorldActions.Sharing).
        /// </summary>
        public static bool Loading =>
            Core.loadingGame || !Core.worldGenFinished() || (Singleton<SaveManager>.Instance != null && Singleton<SaveManager>.Instance.dontFireTriggers);

        /// <summary>
        /// In a world that has finished loading: the player exists from early in the load, but the save (where they
        /// are, what they carry) is only applied as it ends. Stays true once it has been for a couple of seconds.
        /// </summary>
        public static bool WorldReady
        {
            get
            {
                bool now = InWorld && Core.worldGenFinished() && !Core.loadingGame && !Core.forbidInputs;
                if (!now) { _readySince = -1f; return false; }
                if (_readySince < 0f) _readySince = Time.realtimeSinceStartup;
                return Time.realtimeSinceStartup - _readySince > 2f;
            }
        }
        private static float _readySince = -1f;

        // =====================================================================
        // Host
        // =====================================================================

        private sealed class Snapshot
        {
            public byte[] Hash;
            public string ProfileJson;
            public List<(string Name, int Size)> Files; // in order, unpacked sizes
            public byte[] Sent;     // what goes over the wire: Deflate of the files back to back (Packing 1), or the files (0)
            public byte Packing;
            public int RawSize;     // the files' sizes added up
            public string Stamp;
            public int IdCounter;   // SaveManager.currentUniqueID in it (SharedIds)
            public int Scene;       // the loaded world it was made in
            public float MadeAt;
            public string Timing;   // how long reading and packing it took (logged on the main thread)
        }

        private Snapshot _hostWorld;
        // What each joiner was offered, and in which loaded world (scene handle): a reload makes the offer stale.
        private readonly Dictionary<ulong, (Snapshot World, int Scene)> _offered = new Dictionary<ulong, (Snapshot, int)>();
        // Joiners waiting for the world being made for them (PrepareWorld), offered it when it's ready.
        private readonly HashSet<ulong> _waiting = new HashSet<ulong>();
        private bool _preparing;
        // A NeedSave for a world we no longer offer that joiner gets one JoinWait, not one per piece in flight.
        private readonly Dictionary<ulong, float> _staleTold = new Dictionary<ulong, float>();

        /// <summary>The JoinWait reason while the host makes a snapshot for the joiner (the client shows its loading screen for it).</summary>
        public const string Preparing = "The host is saving its world for you...";

        /// <summary>True while the host saves a snapshot of its world for a joiner.</summary>
        public static bool Snapshotting { get; private set; }
        private float _lastSnapshot = -999f;
        private const float SnapshotReuse = 20f;

        /// <summary>
        /// The world as it is now, for someone joining: the host's game saves it, as it would save it, into a folder
        /// of the mod's (its own save is left as it was), with the night-time and loading checks forced past.
        /// Sets _snapshotDir to that folder's profile directory, or to null to fall back on the host's last save.
        ///
        /// Only the dynamic part (sav.dat: the player, the clock, every object that moves or changes) is saved. The
        /// static part (savs.dat: the map's layout and its pathfinding graph, about 9 MB, and savch.dat after chapter 1)
        /// is copied from the host's own save, as the game itself does: it writes savs.dat only when the world is made or
        /// a new location has been added (SaveManager.Save), and in that case it writes it into the snapshot too. Saving
        /// it every time used to take most of the snapshot's time, on the host's main thread.
        ///
        /// When the static part was copied, sav.dat is saved by SnapshotSave: the world is read in one frame, as the
        /// game reads it, and written out as JSON on a worker thread (the writing was most of the stop). If that fails,
        /// or the static part has to be saved too, the game's own SaveManager.Save does it all on the main thread.
        /// </summary>
        private IEnumerator SaveSnapshot()
        {
            _snapshotDir = null;
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || sm.dontSave || Core.loadingGame) yield break;
            string dir = Path.Combine(_cacheDir, "..", "host-snapshot");
            string profDir = Path.Combine(dir, "prof" + Core.currentProfile.id);
            if (Time.realtimeSinceStartup - _lastSnapshot < SnapshotReuse && File.Exists(Path.Combine(profDir, "sav.dat"))) { _snapshotDir = profDir; yield break; }
            string ownDir = Path.Combine(sm.baseSaveDirectory, "prof" + Core.currentProfile.id); // before the redirect below
            var lastSaved = sm.lastTimeSaved; bool haveSaved = sm.haveSaved;
            var outside = Singleton<OutsideLocations>.Instance;
            bool newLocation = outside != null && outside.addedNewLocation;
            bool haveStatic = false;
            long copied = 0;
            System.Threading.Tasks.Task<long> writing = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                RemoveDir(dir);
                Directory.CreateDirectory(profDir);
                foreach (var n in new[] { "savs.dat", "savch.dat" })
                {
                    string from = Path.Combine(ownDir, n);
                    if (!File.Exists(from)) continue;
                    File.Copy(from, Path.Combine(profDir, n));
                    if (n == "savs.dat") haveStatic = true;
                }
                copied = sw.ElapsedMilliseconds;
                if (haveStatic && !newLocation && Plugin.Cfg.SnapshotOffThread.Value && !_offThreadBroken)
                {
                    Snapshotting = true;
                    SaveRedirect.BeginTemporary(Path.GetFullPath(dir));
                    try
                    {
                        DragSync.NoteSnapshot(); // the same frame the world is read in
                        writing = SnapshotSave.Begin(sm, Path.Combine(profDir, "sav.dat"));
                        SharedIds.OnSaved(sm.currentUniqueID); // as the patch on SaveManager.Save records it
                    }
                    catch (Exception e)
                    {
                        _offThreadBroken = true; // most likely a game member it uses is missing: don't try again this run
                        Plugin.Log.LogWarning($"Couldn't read the world for a snapshot off the main thread, saving it the game's way: {e.GetBaseException().Message}");
                    }
                    finally
                    {
                        SaveRedirect.EndTemporary();
                        Snapshotting = false;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't snapshot the world, offering the last save instead: {e.Message}");
                RestoreAfterSnapshot(sm, lastSaved, haveSaved, outside, newLocation);
                yield break;
            }
            if (writing != null)
            {
                long stopped = sw.ElapsedMilliseconds; // the main thread's part: copying the static part and reading the world
                LastSnapshotStopMs = stopped;
                while (!writing.IsCompleted) yield return null;
                if (!writing.IsFaulted && File.Exists(Path.Combine(profDir, "sav.dat")))
                {
                    Plugin.Log.LogInfo($"Saved a snapshot of the world for a joiner: the game stopped for {stopped} ms (static part copied from your save in {copied} ms, the world read in {stopped - copied} ms), sav.dat written in {writing.Result} ms on a worker thread");
                    RestoreAfterSnapshot(sm, lastSaved, haveSaved, outside, newLocation);
                    _lastSnapshot = Time.realtimeSinceStartup;
                    _snapshotDir = profDir;
                    LastSnapshotOffThread = true;
                    yield break;
                }
                // Most likely the game changed a list while it was being written. Save it the game's way instead.
                Plugin.Log.LogWarning($"Couldn't write the snapshot off the main thread, saving it the game's way: {writing.Exception?.GetBaseException().Message}");
                if (!InWorld || Core.loadingGame) { RestoreAfterSnapshot(sm, lastSaved, haveSaved, outside, newLocation); yield break; }
            }
            DragSync.NoteSnapshot();
            _snapshotDir = SaveSnapshotOnMainThread(sm, dir, profDir, haveStatic, copied, newLocation);
            RestoreAfterSnapshot(sm, lastSaved, haveSaved, outside, newLocation);
        }

        private string _snapshotDir;
        /// <summary>Set when SnapshotSave couldn't read this game's world: only the game's own save is used after.</summary>
        private static bool _offThreadBroken;
        /// <summary>How long the host's game stopped for the last snapshot, in ms (-1: none yet).</summary>
        public static long LastSnapshotStopMs { get; private set; } = -1;
        /// <summary>Whether the last snapshot's sav.dat was written on a worker thread (SnapshotSave), not by the game's save.</summary>
        public static bool LastSnapshotOffThread { get; private set; }
        /// <summary>The longest host frame while the last joiner joined, in ms (-1: none yet).</summary>
        public static float LastJoinLongestFrameMs { get; private set; } = -1f;

        /// <summary>The snapshot saved by the game's own SaveManager.Save, in one go on the main thread.</summary>
        private string SaveSnapshotOnMainThread(SaveManager sm, string dir, string profDir, bool haveStatic, long copied, bool newLocation)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Snapshotting = true;
                SaveRedirect.BeginTemporary(Path.GetFullPath(dir));
                sm.Save(doJson: true, doSaveProfile: true, force: true, forceSaveStatic: !haveStatic, showSavingIndicator: false, closeAndOpenStadiaSave: false);
                LastSnapshotStopMs = sw.ElapsedMilliseconds + copied;
                LastSnapshotOffThread = false;
                Plugin.Log.LogInfo($"Saved a snapshot of the world for a joiner the game's way: the game stopped for {LastSnapshotStopMs} ms ({(haveStatic ? $"static part copied from your save in {copied} ms" : "static part saved too: your save hasn't got one")}{(newLocation ? ", with a new location's static part" : "")})");
                _lastSnapshot = Time.realtimeSinceStartup;
                return File.Exists(Path.Combine(profDir, "sav.dat")) ? profDir : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't snapshot the world, offering the last save instead: {e.Message}");
                return null;
            }
            finally
            {
                SaveRedirect.EndTemporary();
                Snapshotting = false;
            }
        }

        private static void RestoreAfterSnapshot(SaveManager sm, DateTime lastSaved, bool haveSaved, OutsideLocations outside, bool newLocation)
        {
            sm.lastTimeSaved = lastSaved; sm.haveSaved = haveSaved; // the menu's "progress since your last save" is about your own save
            // The game clears this once it has saved the static part; that was the snapshot's, so the host's own next
            // save must still write its own.
            if (outside != null && newLocation) outside.addedNewLocation = true;
        }

        /// <summary>The world made for joiners lately, if it's still the one to give them (null: make another).</summary>
        private Snapshot FreshHostWorld()
        {
            var w = _hostWorld;
            if (w == null || w.Scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) return null;
            if (Plugin.Cfg.LiveWorld.Value) return Time.realtimeSinceStartup - w.MadeAt < SnapshotReuse ? w : null;
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null) return null;
            return WorldFiles.Stamp(Path.Combine(sm.baseSaveDirectory, "prof" + Core.currentProfile.id)) == w.Stamp ? w : null;
        }

        /// <summary>
        /// Host: makes the world to give joiners, without stopping the game for longer than the game's own save of it
        /// takes. The snapshot (SaveSnapshot) has to be on the main thread, as it reads the game's objects; reading the
        /// files back, hashing them and packing them (Deflate: Darkwood's JSON saves shrink to about a quarter) run on a
        /// worker thread while the game goes on. Then everyone waiting for it is offered it.
        /// </summary>
        private IEnumerator PrepareWorld()
        {
            _preparing = true;
            yield return null; // the JoinWait goes out first
            Snapshot world = null;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            var sm = Singleton<SaveManager>.Instance;
            if (InWorld && sm != null)
            {
                string snapDir = null;
                if (Plugin.Cfg.LiveWorld.Value) { yield return SaveSnapshot(); snapDir = _snapshotDir; }
                else DragSync.ForgetSnapshot(); // the last save: where things were then isn't known
                string profDir = snapDir ?? Path.Combine(sm.baseSaveDirectory, "prof" + Core.currentProfile.id);
                string stamp = WorldFiles.Stamp(profDir);
                if (stamp.Length > 0 && _hostWorld != null && _hostWorld.Stamp == stamp && _hostWorld.Scene == scene) world = _hostWorld;
                else if (stamp.Length > 0)
                {
                    string json = JsonConvert.SerializeObject(Core.currentProfile);
                    int ids = SharedIds.CounterOf(snapDir != null);
                    int day = Core.currentProfile.day;
                    var job = System.Threading.Tasks.Task.Run(() => Pack(profDir, json));
                    while (!job.IsCompleted) yield return null;
                    if (job.IsFaulted) Plugin.Log.LogWarning($"Couldn't read the world for joiners: {job.Exception?.GetBaseException().Message}");
                    else if ((world = job.Result) != null)
                    {
                        world.Stamp = stamp;
                        world.IdCounter = ids;
                        Plugin.Log.LogInfo($"Host world {WorldFiles.Hex(world.Hash).Substring(0, 12)}, day {day}: {string.Join(", ", world.Files.Select(f => $"{f.Name} {f.Size / 1024} KiB"))}; " +
                                           $"sent as {world.Sent.Length / 1024} KiB{(world.Packing == 1 ? " (Deflate)" : "")}; {world.Timing}");
                    }
                }
            }
            if (world != null)
            {
                world.Scene = scene;
                world.MadeAt = Time.realtimeSinceStartup;
                _hostWorld = world;
            }
            _preparing = false;

            var waiting = _waiting.ToList();
            _waiting.Clear();
            foreach (var peer in waiting)
            {
                if (world == null) _session.Refuse(peer, "The host isn't in a saved game yet. Join once they've loaded their world.");
                else Offer(peer, world);
            }
        }

        /// <summary>Worker thread: read a profile folder's save files, hash them and pack them for sending.</summary>
        private static Snapshot Pack(string profDir, string json)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var files = WorldFiles.Read(profDir);
            if (!files.Any(f => f.Name == "sav.dat")) return null;
            var hash = WorldFiles.Hash(json, files);
            var raw = new byte[files.Sum(f => (long)f.Data.Length)];
            int at = 0;
            foreach (var f in files) { Buffer.BlockCopy(f.Data, 0, raw, at, f.Data.Length); at += f.Data.Length; }
            long readMs = sw.ElapsedMilliseconds;
            byte[] sent = raw; byte packing = 0; string packError = null;
            try
            {
                var packed = WorldFiles.Deflate(raw);
                if (packed.Length < raw.Length) { sent = packed; packing = 1; }
            }
            catch (Exception e) { packError = e.Message; }
            return new Snapshot
            {
                Hash = hash, ProfileJson = json, Files = files.Select(f => (f.Name, f.Data.Length)).ToList(),
                Sent = sent, Packing = packing, RawSize = raw.Length,
                Timing = $"read and hashed in {readMs} ms, " + (packError == null ? $"packed in {sw.ElapsedMilliseconds - readMs} ms" : $"couldn't pack it ({packError})") + " on a worker thread",
            };
        }

        private void Offer(ulong peer, Snapshot world)
        {
            _offered[peer] = (world, world.Scene);
            _staleTold.Remove(peer);
            var w = _session.Begin(MsgType.SaveOffer);
            w.WriteBytes(world.Hash);
            w.W.Write((uint)world.Sent.Length);
            w.WriteBigStr(world.ProfileJson);
            w.W.Write((byte)world.Files.Count);
            foreach (var f in world.Files) { w.WriteStr(f.Name); w.W.Write((uint)f.Size); }
            w.W.Write(world.Packing);
            w.W.Write((uint)world.RawSize);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo($"Offered the world to #{peer} ({world.Sent.Length / 1024} KiB to send, {world.RawSize / 1024} KiB unpacked)");
            WatchFrames(peer);
        }

        // ---- the host's frame times while someone joins ---------------------------------------

        private float _watchUntil = -1f, _watchFrom, _longestFrame;
        private int _slowFrames, _frames;
        private ulong _watchPeer;

        /// <summary>Host: note the longest frame from now until the joiner is in (or a couple of minutes), for the log.</summary>
        private void WatchFrames(ulong peer)
        {
            if (_watchUntil < 0f) { _watchFrom = Time.realtimeSinceStartup; _longestFrame = 0f; _slowFrames = 0; _frames = 0; }
            _watchPeer = peer;
            _watchUntil = Time.realtimeSinceStartup + 120f;
        }

        private void EndWatch(string why)
        {
            if (_watchUntil < 0f) return;
            Plugin.Log.LogInfo($"[join] host frames while #{_watchPeer} joined ({why}, {Time.realtimeSinceStartup - _watchFrom:0.0} s): longest {_longestFrame * 1000f:0} ms, {_slowFrames} of {_frames} over 50 ms");
            LastJoinLongestFrameMs = _longestFrame * 1000f;
            _watchUntil = -1f;
        }

        private void UpdateWatch()
        {
            if (_watchUntil < 0f) return;
            float dt = Time.unscaledDeltaTime;
            _frames++;
            if (dt > _longestFrame) _longestFrame = dt;
            if (dt > 0.05f) _slowFrames++;
            if (Time.realtimeSinceStartup > _watchUntil) EndWatch("not in after 2 minutes");
        }

        /// <summary>
        /// Why a joiner can't come in yet, or null. The prologue, and every dream, is a place of its own the
        /// game loads for the player who's in it; someone joining then would be put where the host stands, in a
        /// place their game hasn't got, and be sent to load again when the host comes out.
        /// </summary>
        private static string HostNotReady()
        {
            if (!InWorld) return null; // the main menu: refused below
            if (TestHoldSecs > 0f)
            {
                if (_testHoldFrom < 0f) _testHoldFrom = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _testHoldFrom < TestHoldSecs) return Prologue;
                TestHoldSecs = 0f;
            }
            // The prologue on a new game: the intro film (WorldGenerator.playingIntro), then the forest
            // (dream_tutorial_00, a separate location), then a dream (dream_tutorial_01).
            var gen = Singleton<WorldGenerator>.Instance;
            var outsideNow = Singleton<OutsideLocations>.Instance;
            if ((gen != null && gen.playingIntro)
                || (outsideNow != null && outsideNow.playerInOutsideLocation && outsideNow.currentLocationName == "dream_tutorial_00"))
                return Prologue;
            var dreams = Singleton<Dreams>.Instance;
            if (dreams != null && dreams.dreaming)
                return Core.currentProfile != null && Core.currentProfile.day <= 1 && !Core.currentProfile.skippedPrologue
                    ? Prologue
                    : "The host is in a dream. You'll join when they wake up.";
            // A separate location is loaded only in the host's game, and the joiner would be put beside the host
            // there (Characters), on the world's grid of a game that hasn't loaded it. The snapshot would also be
            // saved while away, which the game itself never does.
            var outside = Singleton<OutsideLocations>.Instance;
            if (outside != null && outside.playerInOutsideLocation) return "The host is away in another place. You'll join when they're back.";
            if (!WorldReady) return "The host's world is loading.";
            return null;
        }

        private const string Prologue = "The host is playing the prologue. You'll join when it's over.";

        /// <summary>Test (map test's "hold-joiners"): for this many seconds from the first joiner, answer as if the
        /// host were in the prologue.</summary>
        internal static float TestHoldSecs;
        private static float _testHoldFrom = -1f;

        private bool HostBeforeWelcome(ulong peer, byte[] clientHash)
        {
            string wait = HostNotReady();
            if (wait != null)
            {
                JoinWait(peer, wait);
                return false;
            }
            // The world we offered this joiner, now downloaded: let them in with it, even if a newer snapshot would be
            // taken now. Over Steam's relay a download can outlast SnapshotReuse; asking for a fresh snapshot then
            // gave a new hash, a new offer and another download, for ever.
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (_offered.TryGetValue(peer, out var offer) && offer.Scene == scene && WorldFiles.Equal(offer.World.Hash, clientHash))
            {
                LetIn(peer, offer.World.IdCounter);
                return true;
            }
            var world = FreshHostWorld();
            if (world == null)
            {
                // Make one (PrepareWorld), and offer it when it's ready. Until then the joiner is told to wait, and its
                // Hellos meanwhile get the same answer.
                if (!InWorld) { _session.Refuse(peer, "The host isn't in a saved game yet. Join once they've loaded their world."); return false; }
                _waiting.Add(peer);
                if (!_preparing) _runner.StartCoroutine(PrepareWorld());
                WatchFrames(peer);
                JoinWait(peer, Preparing);
                return false;
            }
            if (WorldFiles.Equal(world.Hash, clientHash))
            {
                LetIn(peer, world.IdCounter);
                return true;
            }
            Offer(peer, world);
            return false;
        }

        private void LetIn(ulong peer, int idCounter)
        {
            _offered.Remove(peer);
            _waiting.Remove(peer);
            SharedIds.Joined(peer, idCounter);
            if (_watchPeer == peer) EndWatch("let in");
        }

        private void JoinWait(ulong peer, string why)
        {
            var ww = _session.Begin(MsgType.JoinWait);
            ww.WriteStr(why);
            _session.SendTo(peer, ww, Channel.Control, reliable: true);
        }

        private void OnNeedSave(ulong peer, PacketReader r)
        {
            if (!_session.IsHost) return;
            var hash = r.R.ReadBytes(32);
            uint offset = r.R.ReadUInt32();
            uint length = Math.Min(r.R.ReadUInt32(), (uint)PieceSize);
            if (!_offered.TryGetValue(peer, out var offer) || !WorldFiles.Equal(offer.World.Hash, hash))
            {
                // Not (or no longer) offered to them: the offer went with a dropped connection, or the world was loaded
                // again. Without an answer the joiner would wait for this piece for ever; this has it ask again.
                if (!_staleTold.TryGetValue(peer, out var at) || Time.realtimeSinceStartup - at > 2f)
                {
                    _staleTold[peer] = Time.realtimeSinceStartup;
                    Plugin.Log.LogInfo($"#{peer} asked for a world it isn't offered: telling it to ask again");
                    JoinWait(peer, "The host's world changed while you were getting it: starting again...");
                }
                return;
            }
            var world = offer.World;
            if (offset >= world.Sent.Length) return;
            int n = (int)Math.Min(length, world.Sent.Length - offset);
            var w = _session.Begin(MsgType.SaveData);
            w.WriteBytes(world.Hash);
            w.W.Write(offset);
            w.W.Write((uint)n);
            w.W.Write(world.Sent, (int)offset, n);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
        }

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
            public float StartedAt;  // when the offer came, for the transfer time in the log
            public int Stalls;
        }

        private Download _download;
        private bool _unpacking;
        private byte[] _readyHash;   // the world we downloaded or found, sent in the last Hello

        // A download that gets no piece for StallSecs asks again for every piece it's missing; after MaxStalls of those
        // in a row it gives up, so the loading screen can't wait for ever.
        private const float StallSecs = 10f;
        private const int MaxStalls = 3;

        /// <summary>For the panel: (bytes got, bytes total) while downloading the host's world.</summary>
        public (long Got, long Total)? Progress => _download == null ? ((long, long)?)null : (_download.Received, _download.Blob.Length);
        /// <summary>The download's size unpacked, for the loading screen (0: not packed, or no download).</summary>
        public long UnpackedSize => _download != null && _download.Packing != 0 ? _download.RawSize : 0;
        public string LoadStatus { get; private set; }

        private byte[] ClientHelloHash()
        {
            // The newest world we've got that's still as it came (playing it changes it, as the game saves into it).
            _readyHash = null;
            try
            {
                foreach (var dir in new DirectoryInfo(_cacheDir).GetDirectories().OrderByDescending(d => d.LastWriteTimeUtc))
                {
                    var h = HashOfCached(dir.FullName);
                    if (h != null && WorldFiles.Hex(h) == dir.Name) { _readyHash = h; break; }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't check cached worlds: {e.Message}"); }
            return _readyHash;
        }

        // Hashing a cached world reads all of it (tens of MB), and Hello (sent every 3 s while the host keeps us
        // waiting) asks for it: so each folder's hash is kept until a file in it changes.
        private static readonly Dictionary<string, (string Stamp, byte[] Hash)> _hashCache = new Dictionary<string, (string, byte[])>();

        private static byte[] HashOfCached(string worldDir)
        {
            string stamp;
            try
            {
                long n = 0, bytes = 0, newest = 0;
                foreach (var f in new DirectoryInfo(worldDir).GetFiles("*", SearchOption.AllDirectories))
                {
                    n++; bytes += f.Length; newest = Math.Max(newest, f.LastWriteTimeUtc.Ticks);
                }
                stamp = $"{n}/{bytes}/{newest}";
            }
            catch { return HashOfCachedNow(worldDir); }
            if (_hashCache.TryGetValue(worldDir, out var c) && c.Stamp == stamp) return c.Hash;
            var h = HashOfCachedNow(worldDir);
            _hashCache[worldDir] = (stamp, h);
            return h;
        }

        private static byte[] HashOfCachedNow(string worldDir)
        {
            string pj = Path.Combine(worldDir, "profile.json");
            if (!File.Exists(pj)) return null;
            string json = File.ReadAllText(pj);
            var profile = JsonConvert.DeserializeObject<GameProfile>(json);
            if (profile == null) return null;
            return WorldFiles.Hash(json, WorldFiles.Read(Path.Combine(worldDir, "prof" + profile.id)));
        }

        private void OnSaveOffer(ulong from, PacketReader r)
        {
            if (from != _session.HostId) return;
            var d = new Download { Hash = r.R.ReadBytes(32) };
            // The same world again (an answer to a Hello sent before the first offer came): go on with this download.
            if ((_download != null && WorldFiles.Equal(_download.Hash, d.Hash)) || _unpacking) return;
            uint size = r.R.ReadUInt32();
            d.ProfileJson = r.ReadBigStr();
            int n = r.R.ReadByte();
            d.Files = new List<(string, int)>();
            long sum = 0;
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadStr();
                uint fsize = r.R.ReadUInt32();
                if (Array.IndexOf(WorldFiles.Names, name) < 0 || d.Files.Any(f => f.Name == name))
                {
                    Fail($"the host offered an unexpected file \"{name}\"");
                    return;
                }
                d.Files.Add((name, (int)fsize));
                sum += fsize;
            }
            d.Packing = r.R.ReadByte();
            uint raw = r.R.ReadUInt32();
            if (d.Packing > 1) { Fail($"the host packed its world in a way this version can't read ({d.Packing})"); return; }
            if (sum != raw || raw == 0 || raw > WorldFiles.MaxSize || size == 0 || size > WorldFiles.MaxSize || (d.Packing == 0 && size != raw))
            {
                Fail("the host's world has a bad size");
                return;
            }
            d.RawSize = (int)raw;
            try { d.Profile = JsonConvert.DeserializeObject<GameProfile>(d.ProfileJson); }
            catch (Exception e) { Fail($"couldn't read the host's profile: {e.Message}"); return; }
            if (d.Profile == null || d.Profile.id < 1 || d.Profile.id > 99) { Fail("the host's profile is invalid"); return; }

            d.Blob = new byte[size];
            d.Have = new bool[(size + PieceSize - 1) / PieceSize];
            d.LastPieceAt = d.StartedAt = Time.realtimeSinceStartup;
            _download = d;
            LoadStatus = null;
            Plugin.Log.LogInfo($"Downloading the host's world ({size / 1024} KiB{(d.Packing == 1 ? $", {raw / 1024} KiB unpacked" : "")}, day {d.Profile.day})");
            JoinScreen.Begin();
            for (int i = 0; i < PiecesInFlight; i++) RequestNext();
        }

        private void RequestNext()
        {
            var d = _download;
            if (d == null || d.NextOffset >= d.Blob.Length) return;
            RequestPiece(d, d.NextOffset);
            d.NextOffset += PieceSize;
        }

        private void RequestPiece(Download d, uint offset)
        {
            var w = _session.Begin(MsgType.NeedSave);
            w.WriteBytes(d.Hash);
            w.W.Write(offset);
            w.W.Write((uint)PieceSize);
            _session.SendToHost(w, Channel.Control, reliable: true);
        }

        private void OnSaveData(ulong from, PacketReader r)
        {
            var d = _download;
            if (from != _session.HostId || d == null) return;
            var hash = r.R.ReadBytes(32);
            uint offset = r.R.ReadUInt32();
            int n = (int)r.R.ReadUInt32();
            if (!WorldFiles.Equal(hash, d.Hash)) return; // a piece of an earlier offer, still on its way
            long piece = offset / PieceSize;
            if (n <= 0 || offset % PieceSize != 0 || offset + (long)n > d.Blob.Length) { Fail("bad piece from the host"); return; }
            d.LastPieceAt = Time.realtimeSinceStartup;
            d.Stalls = 0;
            if (d.Have[piece]) return; // asked for twice (after a stall), came twice
            if (r.R.Read(d.Blob, (int)offset, n) != n) { Fail("a short piece from the host"); return; }
            d.Have[piece] = true;
            d.Received += n;
            if (d.Received < d.Blob.Length) { RequestNext(); return; }
            float secs = Time.realtimeSinceStartup - d.StartedAt;
            Plugin.Log.LogInfo($"[join] the world's {d.Blob.Length / 1024} KiB came in {secs * 1000f:0} ms ({d.Blob.Length / 1024f / Mathf.Max(secs, 0.001f):0} KiB/s, {d.Have.Length} pieces)");
            _runner.StartCoroutine(Finish(d));
        }

        /// <summary>Client, every frame: a download that has stopped coming asks again, then gives up.</summary>
        private void UpdateDownload()
        {
            var d = _download;
            if (d == null || Time.realtimeSinceStartup - d.LastPieceAt < StallSecs) return;
            d.LastPieceAt = Time.realtimeSinceStartup;
            if (++d.Stalls > MaxStalls)
            {
                Fail($"the host stopped sending its world ({d.Received / 1024} of {d.Blob.Length / 1024} KiB came)");
                return;
            }
            int asked = 0;
            for (int i = 0; i < d.Have.Length && (uint)i * PieceSize < d.NextOffset; i++)
                if (!d.Have[i]) { RequestPiece(d, (uint)(i * PieceSize)); asked++; }
            Plugin.Log.LogWarning($"No piece of the host's world for {StallSecs:0} s: asked again for {asked} ({d.Stalls}/{MaxStalls})");
        }

        /// <summary>
        /// The whole world has come: unpack it, check its hash and store it on a worker thread (tens of MB), then ask
        /// to join with it.
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
                var raw = d.Packing == 1 ? WorldFiles.Inflate(d.Blob, d.RawSize) : d.Blob;
                var files = new List<WorldFiles.File>();
                int at = 0;
                foreach (var (name, size) in d.Files)
                {
                    var data = new byte[size];
                    Buffer.BlockCopy(raw, at, data, 0, size);
                    at += size;
                    files.Add(new WorldFiles.File { Name = name, Data = data });
                }
                if (!WorldFiles.Equal(WorldFiles.Hash(d.ProfileJson, files), d.Hash)) return "the world didn't arrive intact (hash mismatch)";
                RemoveDir(dir);
                string profDir = Path.Combine(dir, "prof" + d.Profile.id);
                Directory.CreateDirectory(profDir);
                foreach (var f in files) File.WriteAllBytes(Path.Combine(profDir, f.Name), f.Data);
                File.WriteAllText(Path.Combine(dir, "profile.json"), d.ProfileJson);
                Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow);
                took = sw.ElapsedMilliseconds;
                return null;
            });
            while (!job.IsCompleted) yield return null;
            _unpacking = false;
            string error = job.IsFaulted ? $"couldn't store the world: {job.Exception?.GetBaseException().Message}" : job.Result;
            if (!_session.Active || _session.IsHost) yield break; // stopped joining meanwhile
            if (error != null) { Fail(error); yield break; }
            Plugin.Log.LogInfo($"[join] the world was unpacked, checked and stored in {took} ms on a worker thread");
            try { PruneCache(dir); } catch (Exception e) { Plugin.Log.LogWarning($"Couldn't tidy the world cache: {e.Message}"); }

            Plugin.Log.LogInfo($"Got the host's world {hex.Substring(0, 12)}; asking to join with it");
            LoadStatus = null;
            _readyHash = d.Hash;
            _session.ClientWorldHash = () => _readyHash; // send exactly this one in the next Hello
            _session.SendHello();
            _session.ClientWorldHash = ClientHelloHash;
        }

        private void PruneCache(string keep)
        {
            foreach (var old in new DirectoryInfo(_cacheDir).GetDirectories().OrderByDescending(x => x.LastWriteTimeUtc).Skip(KeepWorlds))
            {
                if (SamePath(old.FullName, keep) || SamePath(old.FullName, SaveRedirect.Dir)) continue; // never the world being played
                try { old.Delete(true); } catch { }
            }
        }

        /// <summary>
        /// Deletes a folder of ours, to be made again at once. On Windows, a file in it that another program has open
        /// for a moment (a virus scanner, the search indexer) makes Directory.Delete fail, or leaves the folder waiting
        /// to be deleted so that making it again fails. So it's renamed out of the way first; if it can't be deleted
        /// then, what's left is deleted the next time.
        /// </summary>
        internal static void RemoveDir(string dir)
        {
            dir = Full(dir);
            string parent = Path.GetDirectoryName(dir), aside = Path.GetFileName(dir) + ".old-";
            if (parent == null) { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
            if (Directory.Exists(parent))
                foreach (var old in Directory.GetDirectories(parent, aside + "*")) { try { Directory.Delete(old, true); } catch { } }
            if (!Directory.Exists(dir)) return;
            string moved = Path.Combine(parent, aside + DateTime.UtcNow.Ticks);
            try { Directory.Move(dir, moved); }
            catch (Exception) { Directory.Delete(dir, true); return; } // can't be moved: delete it where it is
            try { Directory.Delete(moved, true); } catch { }
        }

        private static readonly StringComparison PathCase =
            Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static string Full(string p) =>
            Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>The same folder, however the two were written (separators, a trailing slash; case on Windows).</summary>
        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            try { return string.Equals(Full(a), Full(b), PathCase); }
            catch { return a == b; }
        }

        /// <summary>Whether path is dir or inside it.</summary>
        public static bool IsUnder(string path, string dir)
        {
            if (path == null || dir == null) return false;
            try
            {
                string p = Full(path), d = Full(dir);
                return string.Equals(p, d, PathCase) || p.StartsWith(d + Path.DirectorySeparatorChar, PathCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// We're playing a copy of another host's world (from the cache), e.g. after leaving their game. Hosting it
        /// would hand joiners that stale copy as if it were a world of its own, so the panel won't host it.
        /// </summary>
        public bool PlayingHostCopy => SaveRedirect.Active && IsUnder(SaveRedirect.Dir, _cacheDir);

        private void Fail(string why)
        {
            _download = null;
            LoadStatus = $"Couldn't get the host's world: {why}";
            Plugin.Log.LogError(LoadStatus);
            _session.Backend?.Leave();
        }

        // ---- loading the world -------------------------------------------------

        private void OnWelcomed()
        {
            if (_session.IsHost || _readyHash == null) return;
            string dir = Path.Combine(_cacheDir, WorldFiles.Hex(_readyHash));
            JoinScreen.Begin(); // already up if we downloaded the world; this is the first sign if we had it
            _runner.StartCoroutine(LoadWorld(dir));
        }

        private IEnumerator LoadWorld(string worldDir)
        {
            GameProfile profile;
            try { profile = JsonConvert.DeserializeObject<GameProfile>(File.ReadAllText(Path.Combine(worldDir, "profile.json"))); }
            catch (Exception e) { LoadStatus = $"Couldn't read the host's world: {e.Message}"; Plugin.Log.LogError(LoadStatus); JoinScreen.Fail(LoadStatus); yield break; }

            if (!Core.mainMenu || PauseMenuInWorld)
            {
                if (Core.mainMenu) Singleton<MainMenu>.Instance.close();
                LoadStatus = "Leaving your game to join the host's world...";
                Plugin.Log.LogInfo(LoadStatus);
                Core.returnToMainMenu();
            }
            LoadStatus = $"Loading the host's world (day {profile.day})..."; // waiting for the main menu first
            float until = Time.realtimeSinceStartup + 120f;
            while (!MainMenuReady())
            {
                if (Time.realtimeSinceStartup > until) { LoadStatus = "Timed out waiting for the main menu."; JoinScreen.Fail(LoadStatus); yield break; }
                if (!_session.Connected) { LoadStatus = null; yield break; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);

            LoadStatus = $"Loading the host's world (day {profile.day})...";
            Plugin.Log.LogInfo(LoadStatus);
            if (!StartProfile(profile, worldDir))
            {
                LoadStatus = "Couldn't keep the host's world out of your own saves, so it wasn't loaded (see the log).";
                JoinScreen.Fail(LoadStatus);
                _session.Backend?.Leave();
                yield break;
            }
            JoinScreen.HandOver();
        }

        public static bool MainMenuReady()
        {
            var mm = Singleton<MainMenu>.Instance;
            return Core.mainMenu && !PauseMenuInWorld && mm != null && mm.Menu0 != null && mm.Menu0.activeInHierarchy && Singleton<UI>.Instance != null;
        }

        /// <summary>
        /// Start a profile from the main menu as its Play button does, with saves in saveDir (null: your own).
        /// False, and nothing started, if the game would still save somewhere else.
        /// </summary>
        public static bool StartProfile(GameProfile profile, string saveDir)
        {
            if (saveDir != null) SaveRedirect.Begin(saveDir);
            // The menu always has five profile slots; put this one in its own.
            var slots = new List<GameProfile>();
            for (int i = 1; i <= Math.Max(5, profile.id); i++) slots.Add(i == profile.id ? profile : new GameProfile(i, _Active: false, 0));
            Core.profiles = slots;
            Core.currentProfile = profile;
            // The world's save path, as the game builds it. If its baseSaveDirectory was inlined somewhere before the
            // redirect was patched in, it would show here: then don't load, rather than save into the player's own saves.
            if (saveDir != null)
            {
                string save = Singleton<SaveManager>.Instance?.dynamicFile;
                if (!IsUnder(save, saveDir))
                {
                    Plugin.Log.LogError($"The save redirect isn't working: the world would save to {save}, not under {saveDir}");
                    Core.currentProfile = null;
                    return false;
                }
            }
            Singleton<UI>.Instance.StartCoroutine(Singleton<UI>.Instance.initLoadGame());
            return true;
        }

        /// <summary>"--dwmp-continue N": load your own profile N at start (for testing and hosting).</summary>
        public IEnumerator ContinueOwnProfile(int id, string sandboxDir = null)
        {
            while (!MainMenuReady()) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            // "--dwmp-sandbox DIR": play a copy of the saves in DIR (laid out as 1_4Save), never your own.
            if (sandboxDir != null) SaveRedirect.Begin(sandboxDir);
            var state = Singleton<SaveManager>.Instance.loadGameProfiles();
            var profile = state?.profiles?.FirstOrDefault(p => p != null && p.id == id && p.Active);
            if (profile == null) { Plugin.Log.LogError($"--dwmp-continue: no saved profile {id}"); yield break; }
            Plugin.Log.LogInfo($"--dwmp-continue: loading your profile {id} (day {profile.day})");
            Core.profiles = state.profiles;
            Core.currentProfile = profile;
            Singleton<UI>.Instance.StartCoroutine(Singleton<UI>.Instance.initLoadGame());
        }
    }
}

namespace DarkwoodMP.World
{
    /// <summary>The host going back to the main menu stops its server (WorldShare.HostToMainMenu).</summary>
    [HarmonyLib.HarmonyPatch(typeof(Core), nameof(Core.returnToMainMenu))]
    static class HostToMainMenuPatch
    {
        static void Prefix() => WorldShare.Instance?.HostToMainMenu();
    }
}
