using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP
{
    public static class PluginInfo
    {
        public const string Guid = "com.darkwoodmp.coop";
        public const string Name = "DarkwoodMP";
        public const string Version = "0.22.1";
    }

    public sealed class ModConfig
    {
        public ConfigEntry<int> SendRate;
        public ConfigEntry<bool> LogPlayerSprites;
        public ConfigEntry<bool> SkipIntro;
        public ConfigEntry<string> PlayerName;
        public ConfigEntry<int> DirectPort;
        public ConfigEntry<string> LastAddress;
        public ConfigEntry<string> Password;
        public ConfigEntry<string> ServerName;
        public ConfigEntry<int> MaxPlayers;
        public ConfigEntry<bool> LiveWorld;
        public ConfigEntry<bool> SnapshotOffThread;
        public ConfigEntry<string> NewCharacters;
        public ConfigEntry<KeyCode> SkillsKey;
        public ConfigEntry<string> HostWith;
    }

    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        public static Plugin Instance { get; private set; }
        public static ModConfig Cfg { get; private set; }
        public static ConfigEntry<int> SendRate => Cfg.SendRate;
        public static ConfigEntry<bool> LogPlayerSprites => Cfg.LogPlayerSprites;
        public static GUIStyle NameTagStyle;

        private Harmony _harmony;
        private Session _session;
        private PlayerSync _playerSync;
        private PlayerFx _playerFx;
        private TcpBackend _tcp;
        private ServerBrowser _browser;
        /// <summary>The Direct panel's server browser (the map test's "lan-search" uses it).</summary>
        internal static ServerBrowser Browser => Instance?._browser;
        private DirectPanel _direct;
        private WorldShare _world;
        private NetEnemies _enemies;
        private TimeSync _time;
        private Containers _containers;
        private Characters _characters;
        private FlagsSync _flags;
        private DragSync _drag;
        private FuelSync _fuel;
        private WorldEvents _events;
        private ThrowSync _throws;
        private FireSync _fire;
        private bool _wasInWorld;
        private bool? _cursorBefore; // the system cursor's visibility before the panel showed it
        // Typed as an interface on purpose: no Steamworks type may appear in this class (GOG builds).
        private INetBackend _steam;
        private FieldInfo _steamInstance, _steamInitialized; // SteamManager.s_instance and its m_bInitialized
        private bool _steamUnavailable;
        private float _nextSteamCheck;

        private string _spawnType;
        private bool _showMenu;
        private bool _buttonsOffByUs;

        /// <summary>
        /// Why you can't host yet, or null if you can. A host shares the world it's in, so hosting starts from
        /// inside your world; from the main menu a server would only turn joiners away.
        /// </summary>
        public static string CantHostReason =>
            !WorldShare.InWorld ? "To host, load your world first (Start Game), then press Esc and choose Multiplayer in the pause menu."
            // After leaving someone's game you're still in your copy of their world (your own saves are untouched).
            : WorldShare.Instance?.PlayingHostCopy == true ? "This is your copy of another host's world. To host, go back to the main menu and load your own world."
            : null;

        /// <summary>Open the co-op panel (Multiplayer in the pause menu).</summary>
        public void OpenPanel() => SetPanel(true);

        /// <summary>
        /// The main menu's Join Game: the server browser alone. Once a join starts the Multiplayer window opens to show
        /// how it goes (DirectPanel.OnJoining); closing the browser before that closes the co-op windows (Update).
        /// </summary>
        public void OpenJoin()
        {
            _showMenu = true;
            _panel.Open = false;
            _direct.OpenServerBrowser();
        }

        /// <summary>The co-op panel is open.</summary>
        public bool PanelOpen => _showMenu;

        /// <summary>The session (for the menus, which say what leaving it does).</summary>
        public Session Session => _session;

        /// <summary>Steam is running and its backend is up (the Start Game window offers hosting with it).</summary>
        internal bool SteamAvailable => _steam is IGuiSection;
        /// <summary>Steam isn't there at all (GOG), as opposed to not up yet.</summary>
        internal bool SteamMissing => _steamUnavailable;

        /// <summary>
        /// The Start Game window's host box: host with Steam or Direct once the world that's starting has loaded,
        /// as Multiplayer in the pause menu would. Gives up if the game is back at the main menu instead.
        /// </summary>
        internal void HostWhenReady(bool steam) => StartCoroutine(HostWhenWorldReady(steam));

        private System.Collections.IEnumerator HostWhenWorldReady(bool steam)
        {
            float t0 = Time.realtimeSinceStartup;
            while (WorldShare.MainMenuReady() && Time.realtimeSinceStartup - t0 < 20f) yield return null; // the load starts
            while (!WorldShare.WorldReady)
            {
                if (WorldShare.MainMenuReady()) { Log.LogWarning("[start game] back at the main menu before the world loaded: not hosting"); yield break; }
                yield return null;
            }
            if (_session.Active) yield break;
            if (CantHostReason is string why) { Log.LogWarning($"[start game] can't host: {why}"); yield break; }
            if (steam && _steam is IGuiSection steamGui) { steamGui.Host(); Log.LogInfo("[start game] hosting with Steam"); }
            else
            {
                _session.LastRefusal = null;
                bool ok = _tcp.Host(Cfg.ServerName.Value, Cfg.DirectPort.Value, Cfg.Password.Value, Cfg.MaxPlayers.Value);
                Log.LogInfo($"[start game] hosting with Direct on port {Cfg.DirectPort.Value}: {(ok ? "started" : _tcp.Status)}");
            }
        }

        internal void SetPanel(bool show)
        {
            _showMenu = show;
            if (show) { _panel.ResetPlace(); _panel.Show(); } // in its default place each time it's opened
            else { _panel.Open = false; _direct.CloseAll(); }
        }

        /// <summary>
        /// Whether the game was started for the tests (a --dwmp-test... argument, the test world, or tools/run-test.sh's
        /// test control). Only then does the Multiplayer window offer the test world and its tools.
        /// </summary>
        public static bool TestMode { get; private set; }

        private UiWindow _panel;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Cfg = new ModConfig
            {
                PlayerName = Config.Bind("General", "PlayerName", "", "Name shown to other players. Empty = Steam name, or your OS user name."),
                SendRate = Config.Bind("Network", "SendRate", 20, "Player state updates per second."),
                ServerName = Config.Bind("Host", "ServerName", "Darkwood", "Your server's name in other players' server browsers."),
                DirectPort = Config.Bind("Host", "Port", TcpBackend.DefaultPort, "Port to host on: TCP for the game, UDP for the server browser."),
                Password = Config.Bind("Host", "Password", "", "Password players need to join your server (empty for none)."),
                MaxPlayers = Config.Bind("Host", "MaxPlayers", Protocol.MaxPlayers, "Most players at once, you included."),
                LiveWorld = Config.Bind("Host", "LiveWorld", true, "Players who join get the world as it is now (a snapshot saved for them, your own save untouched). Off: as you last saved it."),
                SnapshotOffThread = Config.Bind("Host", "SnapshotOffThread", true, "Write the snapshot for a joiner on a worker thread, so your game stops only while the world is read. Off: the game saves it its own way, all at once."),
                NewCharacters = Config.Bind("Host", "NewCharacters", "fresh", new ConfigDescription("Someone joining your world for the first time starts with: fresh (no items, full health, beside you) or copy (a copy of your character).", new AcceptableValueList<string>("fresh", "copy"))),
                HostWith = Config.Bind("Host", "HostWith", "steam", new ConfigDescription("How the Start Game window hosts: steam (friends join by invite) or direct (IP and port). Without Steam (GOG) it's always direct.", new AcceptableValueList<string>("steam", "direct"))),
                LastAddress = Config.Bind("Network", "LastAddress", "", "Last server joined directly."),
                SkipIntro = Config.Bind("General", "SkipIntro", true, "Start without the studio logo (about 6 seconds sooner at the main menu)."),
                SkillsKey = Config.Bind("General", "SkillsKey", KeyCode.K, "Key that opens and closes the Skills panel (spend skill points)."),
                LogPlayerSprites = Config.Bind("Debug", "LogPlayerSprites", false, "Log every sprite in the player hierarchy once (which are synced/skipped)."),
            };

            _session = new Session { LocalKey = PlayerKey.Load(Paths.ConfigPath) };
            _tcp = new TcpBackend { PlayerCount = () => _session.PlayerCount };
            _session.Add(_tcp);
            _browser = new ServerBrowser(Paths.ConfigPath);
            _direct = new DirectPanel(_tcp, _browser, _session);
            _direct.OnJoining = () => { if (!_panel.Open) { _panel.ResetPlace(); _panel.Show(); } };
            _panel = new UiWindow(0x0D3A, "Multiplayer")
            {
                MinSize = new Vector2(300, 160),
                FitContent = true,
                // Beside the main menu's words (which MenuEntry moves a little to the left), on the right.
                DefaultRect = () => UiWindow.RightSide(380 * PixelFont.Scale, 300 * PixelFont.Scale, top: 0.16f),
                Body = DrawMenu,
                Footer = r => { if (Ui.Button(Ui.Split(r, 0, 90)[1], "Close")) SetPanel(false); },
                FooterHeight = 13 + 8,
                OnClose = () => SetPanel(false),
            };
            new StartGameWindow();
            SaveBook.Register(_session);
            OptionsWindow.Init();
            _playerSync = new PlayerSync(_session);
            _playerFx = new PlayerFx(_session);
            _session.OnPlayerJoined += n => Notice($"{n} joined");
            _session.OnPlayerLeft += n => Notice($"{n} left");
            _session.OnPlayerJoining += n => Notice($"{n} is joining...");
            _session.OnPlayerStoppedJoining += n => Notice($"{n} stopped joining");
            _playerSync.OnRemoteDied += n => Notice($"{n} died");
            _world = new WorldShare(_session, this, Paths.CachePath);
            SaveRedirect.Init();
            PlayerDamage.Register(_session);
            EnemyAreaHits.Register(_session);
            EnemyFx.Register(_session);
            AfterNight.Register(_session);
            _enemies = new NetEnemies(_session);
            _time = new TimeSync(_session);
            WorldActions.Register(_session);
            Authority.Register(_session);
            _containers = new Containers(_session);
            _characters = new Characters(_session);
            _flags = new FlagsSync(_session);
            _drag = new DragSync(_session);
            DoorPush.Register(_session);
            _fuel = new FuelSync(_session);
            new TraderSync(_session);
            new NpcSync(_session);
            _events = new WorldEvents(_session);
            _throws = new ThrowSync(_session);
            _fire = new FireSync(_session);
            new SlotShare(_session);
            Construction.Register(_session);
            ScriptedEvents.Register(_session);
            Hideouts.Register(_session);
            HostIds.Register(_session);
            NightObjects.Register(_session);
            TalkLock.Register(_session);
            Progression.Register(_session);
            DreamSync.Register(_session);
            Locations.Register(_session);
            FarEvents.Register(_session);
            ViewRects.Register(_session);
            Chapters.Register(_session);
            EggSync.Register(_session);
            InfectionSync.Register(_session);
            EnemyLandings.Register(_session);
            _session.OnSessionEnded += ThrownHits.Clear;
            _session.OnSessionEnded += EnemyAreaHits.Clear;
            UpdateLocalName();

            _harmony = new Harmony(PluginInfo.Guid);
            PatchAll();
            Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded. Multiplayer is in the main menu and the pause menu.");
            // The game skips its logo when it comes back to the menu from a world; set the same flag before the first menu.
            if (Cfg.SkipIntro.Value) Core.dontPlayIntroLogos = true;
            StartCoroutine(LogStartTimes());

            var args = Environment.GetCommandLineArgs();
            // "--dwmp-profile-load": time the game's loading code and log what was slowest once a world is ready.
            if (Array.IndexOf(args, "--dwmp-profile-load") >= 0) LoadProfiler.Start(_harmony);
            // "--dwmp-test-control PORT": take test commands from tools/run-test.sh (one game for a whole test run).
            int tc = Array.IndexOf(args, "--dwmp-test-control");
            if (tc >= 0 && tc + 1 < args.Length && int.TryParse(args[tc + 1], out int controlPort)) TestControl.Start(this, controlPort);
            ApplyTestArgs(args);
        }

        private readonly List<Coroutine> _tests = new List<Coroutine>();
        private void StartTest(System.Collections.IEnumerator routine) => _tests.Add(StartCoroutine(routine));

        /// <summary>
        /// The --dwmp-* arguments for hosting, joining and testing: from the command line at start, or from
        /// TestControl before each test of a run.
        /// </summary>
        private static (int w, int h, FullScreenMode mode)? _sizeBefore;

        internal void ApplyTestArgs(string[] args)
        {
            foreach (var a in args) if (a.StartsWith("--dwmp-test")) TestMode = true;
            { int li = Array.IndexOf(args, "--dwmp-load-shots"); LoadingScreen.ShotsDir = li >= 0 && li + 1 < args.Length ? args[li + 1] : null; }
            // "--dwmp-window WxH": play in a window of that size (to check the UI at other sizes); without it, the
            // size from before is put back.
            {
                int wi = Array.IndexOf(args, "--dwmp-window");
                var wh = wi >= 0 && wi + 1 < args.Length ? args[wi + 1].Split('x') : null;
                if (wh != null && wh.Length == 2 && int.TryParse(wh[0], out int ww) && int.TryParse(wh[1], out int hh))
                {
                    if (_sizeBefore == null) _sizeBefore = (Screen.width, Screen.height, Screen.fullScreenMode);
                    Screen.SetResolution(ww, hh, FullScreenMode.Windowed);
                    Log.LogInfo($"--dwmp-window: {ww}x{hh}");
                }
                else if (_sizeBefore is var sb && sb.HasValue)
                {
                    Screen.SetResolution(sb.Value.w, sb.Value.h, sb.Value.mode);
                    _sizeBefore = null;
                }
            }
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            bool Has(string name) => Array.IndexOf(args, name) >= 0;
            // "--dwmp-low-quality": Unity's lowest quality level; "--dwmp-fps N": at most N frames a second (vsync off).
            // For the map4 test, where five games share one machine. Only set, never put back (a test-only game).
            // The game sets vsync from its settings as it starts (Core), so Update keeps the cap in place.
            // "--dwmp-test-presence": log the Steam presence text every 5 s even without Steam, to check it.
            if (Has("--dwmp-test-presence")) _testPresence = true;
            if (Has("--dwmp-low-quality")) { QualitySettings.SetQualityLevel(0, true); Log.LogInfo("--dwmp-low-quality: quality level 0"); }
            if (int.TryParse(Arg("--dwmp-fps"), out int fps) && fps > 0) { _fpsCap = fps; Log.LogInfo($"--dwmp-fps: {fps}"); }
            bool Seconds(string name, out float s) => float.TryParse(Arg(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);

            // "--dwmp-host": start hosting directly at once (testing / always-on hosts); "--dwmp-password P" for this time only.
            // "--dwmp-max-players N" (tests only): this many players, even beyond the mod's 4 (map4 with four clients).
            if (Has("--dwmp-host"))
            {
                TcpBackend.TestMaxPlayers = int.TryParse(Arg("--dwmp-max-players"), out int mp) ? mp : 0;
                _tcp.Host(Cfg.ServerName.Value, Cfg.DirectPort.Value, Arg("--dwmp-password") ?? Cfg.Password.Value, TcpBackend.TestMaxPlayers > 0 ? TcpBackend.TestMaxPlayers : Cfg.MaxPlayers.Value);
            }
            // "--dwmp-continue N": load your own profile N once the main menu is up; "--dwmp-sandbox DIR": a copy of the saves in DIR.
            if (int.TryParse(Arg("--dwmp-continue"), out int profileId))
                StartTest(_world.ContinueOwnProfile(profileId, Arg("--dwmp-sandbox")));
            Threat.Debug = Has("--dwmp-debug-threat");
            ModProfiler.Enabled = Has("--dwmp-profile-mod");
            LoadSpeed.Verify = Has("--dwmp-verify-load");
            WorldActions.Debug = Has("--dwmp-debug-world");
            if (Has("--dwmp-test-world")) StartTest(WorldTest.Run());
            // "--dwmp-test-client-actions": as a client, use up a rope and open a door; the scripted host refuses it.
            if (Has("--dwmp-test-client-actions")) StartTest(WorldTest.ClientActions());
            // "--dwmp-steam-test": host a Steam lobby once Steam is up, log it, leave (the most one account can test).
            if (Has("--dwmp-steam-test")) StartTest(SteamSelfTest());
            // "--dwmp-test-reload N": N seconds after a player joins, your game loads its world again (players should follow).
            if (Seconds("--dwmp-test-reload", out float reloadAfter)) StartTest(ReloadAfterJoin(reloadAfter));
            // "--dwmp-test-death": 6 seconds after a player joins, your character dies (for testing death with others in the world).
            if (Has("--dwmp-test-death")) StartTest(DieAfterJoin());
            // "--dwmp-test-fx": 4 seconds after a player joins, your character is hit for 1 (blood, a pain sound)
            // and burns for 3 seconds without damage, so they see and hear it on your puppet.
            if (Has("--dwmp-test-fx")) StartTest(FxAfterJoin());
            // "--dwmp-test-night": once a player is in, send a night worm and a night attacker at them (logged "[night test]").
            if (Has("--dwmp-test-night")) StartTest(Combat.NightSpawns.TestAfterJoin());
            // "--dwmp-test-menu N": N seconds after a player joins, your game goes back to the main menu (the server should stop).
            if (Seconds("--dwmp-test-menu", out float menuAfter)) StartTest(MenuAfterJoin(menuAfter));
            // "--dwmp-test-chapter": once your world is ready, log the triggers in your world that start a chapter
            // ("[chapter test] trigger KIND ID INDEX at X Z"), for a scripted player to ask for.
            if (Has("--dwmp-test-chapter")) StartTest(ChapterTriggers());
            // "--dwmp-test-end N": N seconds after a player joins, your game starts the epilogue (the game's end).
            if (Seconds("--dwmp-test-end", out float endAfter)) StartTest(EndAfterJoin(endAfter));
            // "--dwmp-test-quick-end": as a joiner, skip the epilogue and the credits soon after they start.
            Chapters.TestQuick = Has("--dwmp-test-quick-end");
            // "--dwmp-test-save N": N seconds after a player joins, the host's game saves (as at dawn), writing their characters.
            if (Seconds("--dwmp-test-save", out float saveAfter)) StartTest(SaveAfterJoin(saveAfter));
            _spawnType = Arg("--dwmp-spawn");
            // "--dwmp-test-blast": once a player joins, an enemy bomb, damage aura and flame go off at them (see Combat.AreaHitsTest).
            if (Has("--dwmp-test-blast")) StartTest(AreaHitsTest.Run());
            if (Has("--dwmp-test-blast")) StartTest(EnemyFxTest.Run());
            // "--dwmp-test-away" (with --dwmp-dummy): knock an object near you, then be away (as in a separate location)
            // for a few seconds; logs the push sent and the enemies kept running near the dummy.
            if (Has("--dwmp-test-away")) StartTest(World.AwayTest.Run());
            // "--dwmp-join host:port": join a server once the main menu is up.
            if (Arg("--dwmp-join") is string join) StartTest(JoinWhenReady(join));
            // "--dwmp-dummy": leave a test dummy once in a world.
            if (Has("--dwmp-dummy")) StartTest(DummyWhenInWorld());
            // "--dwmp-test-menu-entry": check that the menu's list takes a click, Join Game in the main menu (or, with
            // --dwmp-continue, Multiplayer in the pause menu), and opens the co-op panel.
            if (Has("--dwmp-test-menu-entry")) StartTest(MenuEntryTest(Has("--dwmp-continue"), Arg("--dwmp-shots")));
            // "--dwmp-screen WxH": a window of that size once the main menu is up (for checking other screen shapes;
            // with --dwmp-shots DIR, screenshots of the menu and, with --dwmp-continue, the world and pause menu).
            // "--dwmp-test-start-window": open the Start Game window from the main menu (see StartGameWindow.Test for
            // --dwmp-sandbox, --dwmp-screen, --dwmp-shots, --dwmp-start, --dwmp-start-host, --dwmp-start-name).
            if (Has("--dwmp-test-start-window"))
                StartTest(StartGameWindow.Test(Arg("--dwmp-sandbox"), Arg("--dwmp-screen"), Arg("--dwmp-shots"), Arg("--dwmp-start"), Arg("--dwmp-start-host"), Arg("--dwmp-start-name")));
            else if (Arg("--dwmp-screen") is string screen) StartTest(ScreenTest(screen, Arg("--dwmp-shots"), Has("--dwmp-continue")));
            // "--dwmp-testworld": start the test world from the main menu ("--dwmp-reset": from a fresh copy); with
            // --dwmp-shots DIR, screenshots of the area once it's built.
            if (Has("--dwmp-testworld")) { StartTest(TestWorld.Start(_world, reset: Has("--dwmp-reset"))); if (Arg("--dwmp-shots") is string tdir) StartTest(TestWorldShots(tdir)); }
            // "--dwmp-dump-prefabs": once in a world, log every prefab the save system knows (name -> path).
            if (Has("--dwmp-dump-prefabs")) StartTest(DumpPrefabs());
            // "--dwmp-dump-ui": log the main menu's objects (and the fonts loaded), for building UI that matches it.
            if (Has("--dwmp-dump-ui")) StartTest(DumpUi());
            // "--dwmp-shots DIR": screenshots of the menus, the Multiplayer window, the server browser and its connect dialog (and, in a world with a player in, the pause menu) into DIR.
            // "--dwmp-solo-test DIR" (with --dwmp-continue N --dwmp-sandbox, no --dwmp-host): playing alone, the pause menu
            // over the world and a skill reset at the stove, with screenshots into DIR.
            // "--dwmp-test-options DIR": the Options window in the main menu (or, with --dwmp-continue, the pause menu):
            // screenshots of every tab at 1920x1080 and 1280x720 into DIR, and a check that a volume and a key changed
            // there reach the game's stored settings, which are then put back as they were ("[options]" lines).
            if (Arg("--dwmp-test-options") is string optDir) StartTest(OptionsTest.Run(optDir, Has("--dwmp-continue")));
            // "--dwmp-test-drag": a window moved and resized by made-up mouse events, slowly and fast ("[drag]" lines).
            if (Has("--dwmp-test-drag")) StartTest(DragTest.Run());
            // "--dwmp-test-drag-real": the test window open for two minutes for a real mouse to drag ("[drag-real]" lines).
            if (Has("--dwmp-test-drag-real")) StartTest(DragTest.RunReal());
            if (Arg("--dwmp-solo-test") is string solo) StartTest(SoloTest(solo));
            if (Arg("--dwmp-shots") is string shots && !Has("--dwmp-test-menu-entry") && !Has("--dwmp-solo-test") && !Has("--dwmp-screen") && !Has("--dwmp-testworld") && !Has("--dwmp-test-start-window")) StartTest(Shots(shots, Has("--dwmp-continue")));
        }

        /// <summary>Between tests: stop what the last one started and leave its session (TestControl's reset).</summary>
        internal void StopTests()
        {
            foreach (var c in _tests) if (c != null) StopCoroutine(c);
            _tests.Clear();
            Threat.Debug = false;
            WorldActions.Debug = false;
            LoadSpeed.Verify = false;
            Chapters.TestQuick = false;
            _spawnType = null;
            ConfirmDialog.Cancel();
            if (_session.Active) _session.Backend.Leave();
        }

        /// <summary>
        /// Logs each time the main menu, and then a world, is ready, and how long it took. The tests wait for these
        /// lines ("Main menu ready", "World ready").
        /// </summary>
        private System.Collections.IEnumerator LogStartTimes()
        {
            bool first = true;
            while (true)
            {
                while (!WorldShare.MainMenuReady()) yield return null;
                Log.LogInfo($"Main menu ready {Time.realtimeSinceStartup:0.0} s after start");
                while (WorldShare.MainMenuReady()) yield return null;
                float left = Time.realtimeSinceStartup;
                while (!WorldShare.WorldReady && !WorldShare.MainMenuReady()) yield return null;
                if (!WorldShare.WorldReady) continue;
                Log.LogInfo($"World ready {Time.realtimeSinceStartup:0.0} s after start, {Time.realtimeSinceStartup - left:0.0} s after leaving the menu");
                if (first) LoadProfiler.Report();
                if (first && LoadSpeed.Verify) Log.LogInfo($"LoadSpeed: checked {LoadSpeed.Checked} objects' grid nodes, {LoadSpeed.Mismatches} different from the game's search");
                first = false;
                while (WorldShare.InWorld) yield return null;
            }
        }

        private System.Collections.IEnumerator Shots(string dir, bool inWorldToo)
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Collections.IEnumerator Shot(string name)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                string path = System.IO.Path.Combine(dir, name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSecondsRealtime(0.5f);
                Log.LogInfo($"[shots] {path}");
            }
            if (!inWorldToo)
            {
                while (!WorldShare.MainMenuReady()) yield return null;
                yield return new WaitForSecondsRealtime(4f);
                yield return Shot("main-menu");
                Log.LogInfo($"[shots] {MenuList.Check()}");
                MenuList.Click("Quit");
                yield return Shot("main-menu-quit");
                ConfirmDialog.Cancel();
                MenuList.Click("Join Game");
                yield return Shot("main-menu-join");
                SetPanel(false);
                OpenPanel();
                yield return Shot("main-menu-panel");
                _direct.ShowForShots(address: false);
                yield return Shot("main-menu-browser");
                _direct.ShowForShots(address: true);
                yield return Shot("main-menu-connect");
                SetPanel(false);
            }
            if (!inWorldToo) { Log.LogInfo("[shots] done"); yield break; }
            while (!WorldShare.WorldReady) yield return null;
            while (_session.Active && _session.Peers.Count == 0) yield return null;
            yield return new WaitForSecondsRealtime(8f);
            yield return Shot("world");
            Core.showHidePauseMenu();
            float clockBefore = Singleton<Controller>.Instance.CurrentTime;
            yield return Shot("pause-menu");
            Log.LogInfo($"[shots] {MenuList.Check()}");
            MenuList.Click("Back to main menu");
            yield return Shot("pause-menu-confirm");
            ConfirmDialog.Cancel();
            yield return new WaitForSecondsRealtime(1.5f);
            Log.LogInfo($"[shots] pause menu open: time scale {Time.timeScale}, paused {Core.Paused}, clock {clockBefore} -> {Singleton<Controller>.Instance.CurrentTime}");
            OpenPanel();
            yield return Shot("pause-menu-panel");
            SetPanel(false);
            Core.showHidePauseMenu();
            yield return Shot("world-after");
            Log.LogInfo("[shots] done");
        }

        private System.Collections.IEnumerator SoloTest(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Collections.IEnumerator Shot(string name)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
                Log.LogInfo($"[solo] shot {name}");
            }
            while (!WorldShare.WorldReady) yield return null;
            yield return new WaitForSecondsRealtime(6f);
            Log.LogInfo($"[solo] session active {_session.Active}");
            yield return Shot("solo-world");
            Core.showHidePauseMenu();
            yield return Shot("solo-pause");
            var cam = Singleton<CamMain>.Instance;
            Log.LogInfo($"[solo] pause menu: world camera on {cam != null && cam.gameObject.activeSelf}, time scale {Time.timeScale}, paused {Core.Paused}");
            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(1f);

            // The game's keys: which actions K and N are on (the Skills panel's key must be free).
            var controls = Singleton<MainMenu>.Instance != null ? Singleton<MainMenu>.Instance.ControlsMenu : null;
            if (controls != null)
            {
                var keys = new List<string>();
                foreach (var a in "Aim|Attack|Context Menu|Cycle|Dodge|Effects Cycle|Far Look|Hotbar1|Hotbar2|Hotbar3|Hotbar4|Hotbar5|Hotbar6|Hotbar7|Hotbar8|Hotbar9|Inventory|Journal|Jump|Map|Move Horizontal|Move Vertical|Player Menu|Primary Action|Reload|Run|Secondary Attack|Skill Menu|Start|CycleLight|ToggleLightFlash|Menu".Split('|'))
                {
                    try { keys.Add($"{a}={controls.getButtonName(a)}"); } catch (Exception e) { keys.Add($"{a}=? {e.GetType().Name}"); }
                }
                Log.LogInfo($"[solo] the game's keys: {string.Join(", ", keys)}");
            }
            Log.LogInfo($"[solo] skill tiers: {SkillTree.Describe()}");
            var p = Player.Instance;
            var ctl = Singleton<Controller>.Instance;
            var dreams = Singleton<Dreams>.Instance;
            bool dreamsWere = dreams.disabled;
            dreams.disabled = true; // the levels given here mustn't start the level dreams

            // The Skills panel: two levels with their points, the panel opened with its key, a point spent.
            Progression.TestGiveLevels(2, chooseSkills: false);
            Log.LogInfo($"[solo] given two levels: {Progression.TestState()}");
            SkillsPanel.TestPressKey();
            yield return new WaitForSecondsRealtime(2f);
            Log.LogInfo($"[solo] skills panel open {SkillsPanel.Open}, backpack open {p.Inventory.open}");
            yield return Shot("solo-skills");
            var choice = SkillTree.AnyChoice(p);
            int pointsBefore = SkillTree.Points(p);
            if (choice == null) Log.LogInfo("[solo] FAIL no skill to learn");
            else
            {
                SkillsPanel.TestPick(choice[0], choice[1]);
                yield return Shot("solo-skills-picked");
                string why = SkillsPanel.TestLearn(choice[0], choice[1]);
                Log.LogInfo($"[solo] learned {choice[0]}{(choice[1] != null ? " with " + choice[1] : "")}: {(why == null ? "ok" : "FAIL " + why)}, points {pointsBefore} -> {SkillTree.Points(p)}");
                yield return Shot("solo-skills-learned");
                // Against the rules: the same skill again.
                string again = SkillsPanel.TestLearn(choice[0], choice[1]);
                Log.LogInfo($"[solo] the same skill again: {(again != null ? "refused (" + again + ")" : "FAIL learned")}");

                // The mouse (the panel's test mouse) on the learned icon's bottom right pixel.
                if (SkillsPanel.TestRects(choice[0], out var icon, out var cell))
                {
                    SkillsPanel.TestMouse = new Vector2(icon.xMax - 1f, icon.yMax - 1f);
                    yield return Shot("solo-skills-hover-edge");
                    Log.LogInfo($"[solo] test mouse at {choice[0]}'s icon edge: over {SkillsPanel.TestHover ?? "nothing"}; icon {icon}, cell {cell}");
                    SkillsPanel.TestMouse = null;
                }
                // Hovering every skill in turn mustn't change the window, its scrollbar or the grid's place.
                yield return SkillsPanel.TestHoverSweep(null);
                // The real mouse: tools outside the game (xdotool) move it and click on the line below.
                var next = SkillTree.AnyChoice(p);
                if (next != null && SkillsPanel.TestRects(next[0], out var nIcon, out _))
                {
                    int eaten = SkillsPanel.TestEaten;
                    Log.LogInfo($"[solo] real-mouse {Mathf.RoundToInt(nIcon.xMin + 1)} {Mathf.RoundToInt(nIcon.yMin + 1)} click (top left pixel of {next[0]})");
                    yield return new WaitForSecondsRealtime(6f);
                    Log.LogInfo($"[solo] real mouse: over {SkillsPanel.TestHover ?? "nothing"}, chosen {SkillsPanel.TestChosen ?? "nothing"}, the game's mouse reads eaten {SkillsPanel.TestEaten - eaten}");
                    yield return Shot("solo-skills-real-mouse");
                }
            }
            SkillsPanel.TestPressKey();
            yield return new WaitForSecondsRealtime(1.5f);
            Log.LogInfo($"[solo] skills panel open {SkillsPanel.Open}, backpack open {p.Inventory.open}");

            // The stove: a reset outside the morning is refused; in the morning it's allowed, once a day.
            Singleton<UI>.Instance.LevelingMenu.show();
            float until = Time.realtimeSinceStartup + 20f;
            while (!Progression.TestCanReset && Time.realtimeSinceStartup < until) yield return null;
            bool morningWas = ctl.isAfterNight;
            ctl.isAfterNight = false;
            Log.LogInfo($"[solo] stove menu open {Progression.TestCanReset}; before: {Progression.TestState()}");
            yield return Shot("solo-stove");
            Progression.TestReset();
            yield return new WaitForSecondsRealtime(1f);
            Log.LogInfo($"[solo] after a reset outside the morning: {Progression.TestState()}");
            ctl.isAfterNight = true; // as after dawn in the hideout (Controller.startAfterNight), without the trader
            yield return Shot("solo-stove-morning");
            Progression.TestReset();
            yield return new WaitForSecondsRealtime(1f);
            Log.LogInfo($"[solo] after a reset in the morning: {Progression.TestState()}");
            yield return Shot("solo-stove-reset");
            Progression.TestReset();
            yield return new WaitForSecondsRealtime(0.5f);
            Log.LogInfo($"[solo] after a second reset the same day: {Progression.TestState()}");
            ctl.isAfterNight = morningWas;

            // A full bar at the stove gives the level and its point, without the game's skill choice.
            int levelBefore = p.currentLevel;
            p.experience = Mathf.Max(p.experience, p.levelRequirements[Mathf.Clamp(p.currentLevel, 0, p.levelRequirements.Count - 1)]);
            Progression.Cooked(1);
            Singleton<UI>.Instance.LevelingMenu.updateLiquid();
            until = Time.realtimeSinceStartup + 10f;
            while (p.currentLevel == levelBefore && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            Log.LogInfo($"[solo] after a full bar at the stove: level {levelBefore} -> {p.currentLevel}, the game's skill choice open {Singleton<SkillsMenu>.Instance.opened}: {Progression.TestState()}");
            yield return Shot("solo-stove-level");
            Singleton<UI>.Instance.LevelingMenu.hide();
            yield return new WaitForSecondsRealtime(2f);
            SkillsPanel.TestPressKey();
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("solo-skills-after");
            SkillsPanel.TestPressKey();
            dreams.disabled = dreamsWere;
            Log.LogInfo("[solo] done");
        }

        private System.Collections.IEnumerator ScreenTest(string size, string shots, bool inWorld)
        {
            var wh = size.Split('x');
            int w = int.Parse(wh[0]), h = int.Parse(wh[1]);
            System.Collections.IEnumerator Shot(string name)
            {
                if (shots == null) yield break;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shots, $"{size}-{name}.png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            // Loading a world sets the game's own resolution again, so in a world the size is set once it's up.
            if (!inWorld)
            {
                while (!WorldShare.MainMenuReady()) yield return null;
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(3f);
                Log.LogInfo($"[screen] asked {w}x{h}, got {Screen.width}x{Screen.height}; mouse {UnityEngine.Cursor.lockState}");
                yield return Shot("menu");
                MenuList.Click("Quit");
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("quit");
                ConfirmDialog.Cancel();
                yield return new WaitForSecondsRealtime(0.5f);
                MenuList.Click("Join Game");
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("panel");
                _direct.ShowForShots(address: false);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("browser");
                SetPanel(false);
                Log.LogInfo("[screen] done");
                yield break;
            }
            while (!WorldShare.WorldReady) yield return null;
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(4f);
            Log.LogInfo($"[screen] in the world: {Screen.width}x{Screen.height}; zoom {Singleton<Controller>.Instance.cameraZoom}; mouse {UnityEngine.Cursor.lockState}");
            yield return Shot("world");
            Player.Instance.openInventory();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("inventory");
            Player.Instance.closeInventory();
            yield return new WaitForSecondsRealtime(1f);
            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("pause");
            Log.LogInfo($"[screen] pause menu: mouse {UnityEngine.Cursor.lockState}");
            MenuList.Click("Quit");
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("pause-quit");
            ConfirmDialog.Cancel();
            yield return new WaitForSecondsRealtime(0.5f);
            Core.showHidePauseMenu();
            Log.LogInfo("[screen] done");
        }

        private System.Collections.IEnumerator MenuEntryTest(bool pauseMenuToo, string shots)
        {
            if (!pauseMenuToo)
            {
                while (!WorldShare.MainMenuReady()) yield return null;
                yield return new WaitForSecondsRealtime(2f);
                yield return CheckMenuEntry("main menu", shots);
                yield break;
            }
            // Loading the world turns the menu's buttons off at once, so the main menu is checked on its own.
            while (!WorldShare.WorldReady) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(2f);
            yield return CheckMenuEntry("pause menu", shots);
            Core.showHidePauseMenu();
        }

        /// <summary>With a screenshot of the panel it opened into DIR, if given.</summary>
        private System.Collections.IEnumerator CheckMenuEntry(string where, string shots)
        {
            Log.LogInfo($"[menu-entry] {where}: {MenuList.Check()}");
            bool took = MenuList.Click(where == "main menu" ? "Join Game" : "Multiplayer");
            yield return null;
            yield return null;
            Log.LogInfo($"[menu-entry] {where}: clicked, panel open: {_showMenu} (entry taken: {took})");
            if (shots != null)
            {
                yield return new WaitForSecondsRealtime(1f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shots, $"panel-{where.Replace(' ', '-')}.png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            SetPanel(false);
            yield return null;
        }

        private System.Collections.IEnumerator TestWorldShots(string dir)
        {
            while (!TestWorld.Active || !WorldShare.WorldReady) yield return null;
            yield return new WaitForSecondsRealtime(6f);
            // Out of the house and turned towards the area's west side, so some of it is in sight.
            var p = Player.Instance.transform;
            p.position += new Vector3(-300f, 0f, 250f);
            yield return new WaitForSecondsRealtime(2f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "testworld-area.png"));
            yield return new WaitForSecondsRealtime(1f);
            OpenPanel();
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "testworld-panel.png"));
            yield return new WaitForSecondsRealtime(1f);
            SetPanel(false);
            // The nearest workbench open, for its storage and the Sort & Stack button under it.
            var wb = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OrderBy(UnityEngine.Object.FindObjectsOfType<Workbench>(), w => (w.transform.position - p.position).sqrMagnitude));
            if (wb != null && wb.normalInventory != null)
            {
                yield return new WaitForSecondsRealtime(1f);
                wb.open();
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "testworld-workbench.png"));
                yield return new WaitForSecondsRealtime(1f);
                Player.Instance.closeInventory();
            }
            Log.LogInfo("[testworld] shots done");
        }

        private System.Collections.IEnumerator DumpPrefabs()
        {
            while (!WorldShare.WorldReady) yield return null;
            var sm = Singleton<SaveManager>.Instance;
            foreach (var kv in sm.prefabDictionary) Log.LogInfo($"[prefab] {kv.Key} = {kv.Value}");
            Log.LogInfo($"[prefab] {sm.prefabDictionary.Count} prefabs");
            var idb = Singleton<ItemsDatabase>.Instance;
            if (idb != null) foreach (var kv in idb.itemsDict) Log.LogInfo($"[item] {kv.Key} = {kv.Value}");
        }

        private System.Collections.IEnumerator DumpUi()
        {
            while (!WorldShare.MainMenuReady()) yield return null;
            yield return new WaitForSeconds(3f);
            var menu = Singleton<MainMenu>.Instance;
            if (menu != null) DumpTree(menu.transform, 0);
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>()) Log.LogInfo($"[ui] font {f.name} dynamic={f.dynamic}");
            var ui = Singleton<UI>.Instance;
            if (ui != null) foreach (Transform c in ui.transform) Log.LogInfo($"[ui] UI child {c.name} active={c.gameObject.activeSelf} pos={c.localPosition}");
            try { foreach (var kv in global::Language.GetAllKeysForSheet("UI")) Log.LogInfo($"[ui] UI key \"{kv.Key}\" = \"{kv.Value}\""); }
            catch (Exception e) { Log.LogWarning($"[ui] no UI sheet: {e.Message}"); }
        }

        private static void DumpTree(Transform t, int depth)
        {
            if (depth > 6) return;
            var comps = string.Join(",", Array.ConvertAll(t.GetComponents<Component>(), c => c == null ? "?" : c.GetType().Name));
            string extra = "";
            var tm = t.GetComponent<tk2dTextMesh>();
            if (tm != null) extra += $" text=\"{tm.text}\" font={tm.font?.name}";
            var sp = t.GetComponent<tk2dBaseSprite>();
            if (sp != null) extra += $" sprite={sp.CurrentSprite?.name} coll={sp.Collection?.spriteCollectionName} color={sp.color}";
            var mb = t.GetComponent<Button>();
            if (mb != null) extra += $" button function={mb.function} localized={mb.localized}";
            Log.LogInfo($"[ui] {new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} layer={t.gameObject.layer} pos={t.localPosition} scale={t.localScale} [{comps}]{extra}");
            foreach (Transform c in t) DumpTree(c, depth + 1);
        }

        private System.Collections.IEnumerator DummyWhenInWorld()
        {
            while (!WorldShare.InWorld) yield return null;
            yield return new WaitForSeconds(5f);
            _playerSync.SpawnDummy(new Vector3(120f, 0f, 0f));
            Log.LogInfo("--dwmp-dummy: dummy left 120 units east of you");
            if (_spawnType == null) yield break;
            yield return new WaitForSeconds(2f);
            // "--dwmp-spawn TYPE": an enemy (a CharacterType name) between you and the dummy, after it.
            NetPlayerProxy.All.TryGetValue(PlayerSync.DummyId, out var dummy);
            // With an offset, spawnCharacterAround puts it at offset * distance: here, north of the point between you two.
            Vector3 mid = dummy != null ? (dummy.transform.position + Player.Instance.transform.position) / 2f : Player.Instance.transform.position;
            Vector3 spot = mid + new Vector3(0f, 0f, 250f);
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(Player.Instance.gameObject, spot, 1f, _spawnType, nocturnal: false, attackPlayer: false);
            Log.LogInfo(c != null ? $"--dwmp-spawn: {c.name} at {c.transform.position}" : $"--dwmp-spawn: couldn't spawn {_spawnType}");
            if (c == null || !Threat.Debug) yield break;
            yield return new WaitForSeconds(3f);
            Log.LogInfo($"[threat test] {c.name}.attackPlayer()");
            c.attackPlayer();
            // Threat test: after a while, you "hit" it, which should earn you its attention.
            yield return new WaitForSeconds(15f);
            if (c != null && c.alive && Player.Instance != null)
            {
                Log.LogInfo($"[threat test] the host player hits {c.name} for 30");
                c.getHit(30f, Player.Instance.transform, canCutInHalf: false, byPlayer: true, canInterrupt: true);
            }
        }

        // Its own coroutine (a class of its own): its Steamworks types are only touched when Steam is there.
        private System.Collections.IEnumerator SteamSelfTest()
        {
            float until = Time.realtimeSinceStartup + 60f;
            while (_steam == null) { if (Time.realtimeSinceStartup > until || _steamUnavailable) { Log.LogWarning("--dwmp-steam-test: no Steam"); yield break; } yield return null; }
            var steam = (SteamBackend)_steam;
            steam.Host();
            until = Time.realtimeSinceStartup + 20f;
            while (!steam.InSession && Time.realtimeSinceStartup < until) yield return null;
            Log.LogInfo($"--dwmp-steam-test: {steam.Status}; host={steam.IsHost}; in session={_session.Active} as #{_session.LocalId}; members: {steam.MemberNames()}");
            yield return new WaitForSeconds(5f);
            steam.Leave();
            Log.LogInfo($"--dwmp-steam-test: left; {steam.Status}");
        }

        private System.Collections.IEnumerator ReloadAfterJoin(float seconds)
        {
            while (_session.Peers.Count == 0) yield return null;
            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-reload: loading the world again");
            Singleton<UI>.Instance.quickLoadGame();
        }

        private System.Collections.IEnumerator ChapterTriggers()
        {
            while (!WorldShare.WorldReady) yield return null;
            int n = 0;
            foreach (var et in Resources.FindObjectsOfTypeAll<EventTriggers>())
            {
                if (et == null || !et.gameObject.scene.IsValid()) continue;
                for (int i = 0; i < et.eventTriggers.Count; i++)
                {
                    var t = et.eventTriggers[i];
                    var ge = t?.gameEvents;
                    if (ge == null || !ge.events.Exists(e => e != null && e.type == GameEvent.Type.transportPlayerToObject && e.activeModifier)) continue;
                    var r = World.ObjRef.Of(et.gameObject);
                    var at = et.transform.position;
                    var step = ge.events.Find(e => e != null && e.type == GameEvent.Type.transportPlayerToObject && e.activeModifier);
                    Log.LogInfo($"[chapter test] trigger {(int)r.K} {r.Id} {i} at {at.x:0} {at.z:0} ({et.name}, {t.type}, to chapter {step.intValue}, fired {t.fired})");
                    n++;
                }
            }
            foreach (var ge in Resources.FindObjectsOfTypeAll<GameEvents>())
                if (ge != null && ge.gameObject.scene.IsValid() && ge.events.Exists(e => e != null && e.type == GameEvent.Type.transportPlayerToObject && e.activeModifier))
                    Log.LogInfo($"[chapter test] events {ge.name} at {ge.transform.position.x:0} {ge.transform.position.z:0}");
            // This world's own chapter event is in a separate location, loaded only when someone goes there; put a
            // stand-in event with the same step beside the host, for a scripted player to ask for.
            var go = new GameObject("dwmp_test_chapter");
            go.transform.position = Player.Instance.transform.position + new Vector3(300f, 0f, 0f);
            var test = go.AddComponent<GameEvents>();
            int next = Chapters.HostChapter + 1;
            test.events.Add(new GameEvent { type = GameEvent.Type.transportPlayerToObject, activeModifier = true, intValue = next, eventAmount = 1 });
            World.ObjRef.RegisterSpawned(990001, go);
            var tr = World.ObjRef.Of(go);
            Log.LogInfo($"[chapter test] events {(int)tr.K} {tr.Id} 0 at {go.transform.position.x:0} {go.transform.position.z:0} to chapter {next}");
            Log.LogInfo($"[chapter test] {n} chapter triggers; this is chapter {Chapters.HostChapter}");
        }

        private System.Collections.IEnumerator EndAfterJoin(float seconds)
        {
            while (_session.Peers.Count == 0 || !WorldShare.WorldReady) yield return null;
            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-end: starting the epilogue");
            // The step the game's last event runs (GameEvent startDream into the epilogue).
            var step = new GameEvent { type = GameEvent.Type.startDream, Value = Chapters.EpilogueDream, eventAmount = 1 };
            Singleton<Controller>.Instance.StartCoroutine(step.fire(Player.Instance.gameObject));
        }

        private System.Collections.IEnumerator MenuAfterJoin(float seconds)
        {
            while (_session.Peers.Count == 0) yield return null;
            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-menu: going back to the main menu");
            Core.returnToMainMenu();
        }

        private System.Collections.IEnumerator DieAfterJoin()
        {
            while (_session.Peers.Count == 0) yield return null;
            yield return new WaitForSeconds(6f);
            Log.LogInfo($"--dwmp-test-death: dying with {Player.Instance.Inventory.getAllItems().Count} items");
            Player.Instance.die();
            yield return new WaitForSeconds(15f);
            Log.LogInfo($"--dwmp-test-death: alive again: {Player.Instance.alive}, at {Player.Instance.transform.position}");
        }

        private System.Collections.IEnumerator FxAfterJoin()
        {
            while (_session.Peers.Count == 0) yield return null;
            yield return new WaitForSeconds(4f);
            var p = Player.Instance;
            Log.LogInfo($"--dwmp-test-fx: hit for 1 (health {p.health:0})");
            p.getHit(1f);
            yield return new WaitForSeconds(1f);
            // A Burn alone shows flames and plays the fire; only the burn effect (CharacterEffect) does damage.
            if (p.GetComponent<Burn>() == null) p.gameObject.AddComponent<Burn>().burnTime = 3f;
            yield return new WaitForSeconds(5f);
            Log.LogInfo($"--dwmp-test-fx: done (health {p.health:0}, burning {p.GetComponent<Burn>() != null})");
        }

        private System.Collections.IEnumerator SaveAfterJoin(float seconds)
        {
            while (_session.Peers.Count == 0) yield return null;
            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-save: saving the world");
            Singleton<SaveManager>.Instance.Save(doJson: true, force: true);
        }

        private System.Collections.IEnumerator JoinWhenReady(string address)
        {
            while (!WorldShare.MainMenuReady()) yield return null;
            Log.LogInfo($"--dwmp-join: joining {address}");
            _tcp.Join(address, Cfg.Password.Value);
        }

        /// <summary>
        /// Patch class by class, so one that doesn't fit this build of the game (a GOG build that differs, a game
        /// update) is reported and skipped instead of stopping every patch after it.
        /// </summary>
        private void PatchAll()
        {
            int ok = 0, failed = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { _harmony.CreateClassProcessor(type).Patch(); ok++; }
                catch (Exception e) { failed++; Log.LogError($"Patch {type.Name} doesn't fit this build of the game, skipped: {e.InnerException?.Message ?? e.Message}"); }
            }
            if (failed > 0) Log.LogWarning($"{failed} of {ok + failed} patches skipped; what they do won't be shared");
            else Log.LogInfo($"All {ok} patches applied");
        }

        /// <summary>After the frame's game code: what the player carries now (for undoing a refused request's change), and the mouse confined only while playing.</summary>
        private void LateUpdate()
        {
            InventoryWatch.LateUpdate();
            JoinScreen.LateUpdate();
            CursorLock.LateUpdate(MenuWindows.AnyOpen);
            GameCursor.LateUpdate(MenuWindows.AnyOpen || SkillsPanel.Open || DragTest.WindowOpen);
        }

        private static int _fpsCap;

        private void Update()
        {
            if (_fpsCap > 0 && (Application.targetFrameRate != _fpsCap || QualitySettings.vSyncCount != 0))
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = _fpsCap;
            }
            if (_steam == null && !_steamUnavailable && Time.unscaledTime >= _nextSteamCheck)
            {
                _nextSteamCheck = Time.unscaledTime + 1f;
                if (SteamReady()) TryCreateSteamBackend();
            }

            // Escape closes the panel, unless a question over it takes it (ConfirmDialog answers No).
            if (_showMenu && !ConfirmDialog.Open && Input.GetKeyDown(KeyCode.Escape)) SetPanel(false);
            // The browser was opened alone (Join Game) and closed without joining: nothing of the co-op windows is left.
            if (_showMenu && !_panel.Open && !_direct.AnyOpen) SetPanel(false);
            MenuEntry.Update();
            LoadProfiler.Frame();
            TestControl.Update();
            // Over the game's menus, its buttons (and its Escape) would take the clicks and keys meant for our windows.
            bool windows = MenuWindows.AnyOpen;
            if (_buttonsOffByUs && !windows) { _buttonsOffByUs = false; if (Singleton<Controller>.Instance != null) Singleton<Controller>.Instance.buttonsDisabled = false; }
            if (windows && Core.mainMenu && Singleton<Controller>.Instance != null && !Singleton<Controller>.Instance.buttonsDisabled)
            { Singleton<Controller>.Instance.buttonsDisabled = true; _buttonsOffByUs = true; }
            // After the buttons are let go: an answered question's action, then the menu's list (and a click on it).
            ConfirmDialog.Update();
            OptionsWindow.Update();
            MenuList.Update();
            ModProfiler.Begin();
            _session.Update(); ModProfiler.Mark("session");
            _browser.Update(); ModProfiler.Mark("browser");
            _playerSync.Update(); ModProfiler.Mark("players");
            _playerFx.Update(); ModProfiler.Mark("player fx");
            Proxies.Update(_playerSync, _session.IsHost); ModProfiler.Mark("proxies");
            _enemies.Update(); ModProfiler.Mark("enemies");
            _time.Update(); ModProfiler.Mark("time");
            _containers.Update(); ModProfiler.Mark("containers");
            _characters.Update(); ModProfiler.Mark("characters");
            _flags.Update(); ModProfiler.Mark("flags");
            _drag.Update(); ModProfiler.Mark("drag");
            DoorPush.Update(); ModProfiler.Mark("door push");
            _fuel.Update(); ModProfiler.Mark("fuel");
            _events.Update(); ModProfiler.Mark("events");
            HostIds.Update(); ModProfiler.Mark("host ids");
            NightObjects.Update(); ModProfiler.Mark("night objects");
            EnemyFx.Update(); ModProfiler.Mark("enemy fx");
            _world.Update(); ModProfiler.Mark("world");
            if (_tcp.IsHost && Time.unscaledTime >= _nextWorldLabel) { _nextWorldLabel = Time.unscaledTime + 2f; _tcp.WorldLabel = WorldLabel(); }
            if ((_steam != null || _testPresence) && Time.unscaledTime >= _nextPresence) { _nextPresence = Time.unscaledTime + 5f; UpdatePresence(); }
            JoinScreen.Update(_session, _world);
            LoadingScreen.Update();
            Chapters.Update();
            Progression.Update();
            SkillsPanel.Update(_showMenu);
            DreamSync.Update();
            Locations.Update();
            ViewRects.Update();
            InfectionSync.Update();
            CoopPause.Update(); ModProfiler.Mark("pause");
            AfterNight.Update();
            if (_session.IsHost && WorldShare.InWorld) TraderSync.Instance?.HostUpdate();
            NpcSync.Instance?.Update(); ModProfiler.Mark("npcs");
            DialogueGuard.Update();
            TestWorld.Update();
            if (WorldShare.InWorld && !_wasInWorld) { Spectator.Reset(); ThrownHits.Clear(); EnemyAreaHits.Clear(); } // a new world
            _wasInWorld = WorldShare.InWorld;
            ModProfiler.EndFrame();
        }

        /// <summary>
        /// Checks the game's own SteamManager via reflection (it may be missing or never init on GOG). Only one the
        /// game made is looked at: SteamManager.Initialized makes a SteamManager when there's none yet, and its Awake
        /// quits the game when steam_api can't be loaded (as on a GOG build that never makes one itself).
        /// </summary>
        private bool SteamReady()
        {
            try
            {
                if (_steamInstance == null)
                {
                    var t = AccessTools.TypeByName("SteamManager");
                    _steamInstance = t?.GetField("s_instance", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    _steamInitialized = t?.GetField("m_bInitialized", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (_steamInstance == null || _steamInitialized == null)
                    {
                        _steamUnavailable = true;
                        Log.LogInfo("No SteamManager in this build: direct IP only");
                        return false;
                    }
                }
                var manager = _steamInstance.GetValue(null) as UnityEngine.Object;
                return manager != null && (bool)_steamInitialized.GetValue(manager);
            }
            catch { return false; } // Steam not running (GOG)
        }

        private void TryCreateSteamBackend()
        {
            try { CreateSteamBackend(); }
            catch (Exception e)
            {
                _steamUnavailable = true;
                Log.LogWarning($"Steam networking unavailable, direct IP only: {e.Message}");
            }
        }

        // Separate, non-inlined method: if Steamworks.NET can't load, only this call fails.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateSteamBackend()
        {
            var steam = new SteamBackend();
            _session.Add(steam);
            _steam = steam;
            UpdateLocalName();
            Log.LogInfo("Steam ready: Steam lobbies and invites enabled");
        }

        /// <summary>Your name (General.PlayerName; empty: the Steam or OS user name), changed in Options.</summary>
        public void SetPlayerName(string name)
        {
            Cfg.PlayerName.Value = name ?? "";
            UpdateLocalName();
        }

        private void UpdateLocalName()
        {
            string name = Cfg.PlayerName.Value;
            if (string.IsNullOrWhiteSpace(name)) name = (_steam as IGuiSection)?.SuggestedPlayerName;
            if (string.IsNullOrWhiteSpace(name)) name = Environment.UserName;
            _session.LocalName = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
        }

        private void OnGUI()
        {
            TestControl.OnGUI();
            if (NameTagStyle == null)
            {
                NameTagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                NameTagStyle.normal.textColor = new Color(0.9f, 0.85f, 0.7f);
            }
            _playerSync.DrawNameTags();
            TestWorld.DrawLabels();
            Spectator.DrawGui();
            Progression.OnGUI();
            SkillsPanel.OnGUI();
            ShowSystemCursor(MenuWindows.AnyOpen || SkillsPanel.Open || DragTest.WindowOpen);
            if (!_showMenu && !MenuList.Shown && !MenuWindows.AnyOpen) { DragTest.OnGUI(); return; }
            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            // The menu's list under the windows, then the co-op windows, the other menu windows, and a question on top.
            MenuList.OnGUI();
            if (_showMenu)
            {
                _panel.Title = $"Multiplayer  -  DarkwoodMP {PluginInfo.Version}";
                _panel.OnGUI();
                _direct.OnGUI();
            }
            MenuWindows.DrawAll();
            ConfirmDialog.OnGUI();
            GUI.skin = skin;
            DragTest.OnGUI();
        }

        /// <summary>
        /// The game draws its cursor as a sprite in the world's UI, which the panel covers. While the panel is open
        /// the system cursor is shown over it; when it closes, the cursor is as the game had it.
        /// </summary>
        private void ShowSystemCursor(bool show)
        {
            if (show)
            {
                if (_cursorBefore == null) _cursorBefore = UnityEngine.Cursor.visible;
                if (!UnityEngine.Cursor.visible) UnityEngine.Cursor.visible = true;
            }
            else if (_cursorBefore != null)
            {
                UnityEngine.Cursor.visible = _cursorBefore.Value;
                _cursorBefore = null;
            }
        }

        private void DrawMenu(Flow f)
        {
            {
                // The name is changed in Options (Multiplayer); here it's only shown.
                var r = f.Row(Ui.RowH, 90, 0, 0);
                Ui.Label(r[0], "Your name");
                Ui.Label(r[1], PixelFont.Fit(_session.LocalName, r[1].width), MenuSkin.Bright);
                if (!_session.Active) Ui.Label(r[2], "change it in Options", MenuSkin.Dim, TextAnchor.MiddleRight);
            }

            if (_world.Progress is var p && p.HasValue)
                Ui.Text(f, $"Downloading the host's world: {p.Value.Got / 1048576f:0.0} / {p.Value.Total / 1048576f:0.0} MB", MenuSkin.Bright);
            else if (_world.LoadStatus != null) Ui.Text(f, _world.LoadStatus, MenuSkin.Bright);
            if (TestWorld.Active) Ui.Text(f, "You're in the test world: your own saves are untouched.", MenuSkin.Dim);
            else if (SaveRedirect.Active) Ui.Text(f, "You're in the host's world: your own saves are untouched.", MenuSkin.Dim);
            else if (WorldShare.InWorld && !_session.Active) Ui.Text(f, "Joining a game leaves the one you're in (save first).", MenuSkin.Dim);
            if (!_session.Active && CantHostReason is string why) Ui.Text(f, why, MenuSkin.Dim);

            Ui.Heading(f, "Steam");
            if (_steam is IGuiSection steamGui) steamGui.DrawGui(_session, f);
            else Ui.Text(f, _steamUnavailable ? "Steam isn't running (the GOG version, say): use a direct connection." : "Waiting for Steam...", MenuSkin.Dim);

            Ui.Heading(f, "Direct connection");
            _direct.Draw(f);

            if (_session.Active && _session.Names.Count > 0)
            {
                Ui.Heading(f, "Players");
                ulong hostId = _session.IsHost ? _session.LocalId : _session.HostId;
                foreach (var kv in _session.Names)
                {
                    string tags = (kv.Key == hostId ? "  (host)" : "") + (kv.Key == _session.LocalId ? "  (you)" : "");
                    Ui.Label(f, kv.Value + tags);
                }
            }

            if (TestMode) DrawTestWorld(f);
        }

        /// <summary>The test world (only when the game was started for the tests): start or reset it from the main menu; in it, the host's tools.</summary>
        private void DrawTestWorld(Flow f)
        {
            bool inMenu = WorldShare.MainMenuReady() && !_session.Active;
            if (!inMenu && !(TestWorld.Active && !_session.IsClient)) return;
            Ui.Heading(f, "Test world");
            if (inMenu)
            {
                Ui.Text(f, "A copy of your saves with a test area of everything that's shared. Host it, and friends join it as usual.", MenuSkin.Dim);
                var r = f.Row(Ui.RowH, 0, 90);
                if (Ui.Button(r[0], TestWorld.Exists ? "Start the test world" : "Make and start a test world")) { SetPanel(false); StartCoroutine(TestWorld.Start(_world, reset: false)); }
                if (TestWorld.Exists && Ui.Button(r[1], "Reset it")) { SetPanel(false); StartCoroutine(TestWorld.Start(_world, reset: true)); }
                if (TestWorld.Status != null) Ui.Text(f, TestWorld.Status);
                return;
            }
            var t = f.Row(Ui.RowH, 0, 0, 0, 0, 0);
            if (Ui.Button(t[0], "Kit")) TestWorld.GiveKit();
            if (Ui.Button(t[1], "Dawn")) TestWorld.SetTime(Singleton<Controller>.Instance.dayTime + 5);
            if (Ui.Button(t[2], "Noon")) TestWorld.SetTime(720);
            if (Ui.Button(t[3], "Dusk")) TestWorld.SetTime(Singleton<Controller>.Instance.nightTime - 30);
            if (Ui.Button(t[4], "Night")) TestWorld.SetTime(Singleton<Controller>.Instance.nightTime + 30);
            Ui.Label(f, "Spawn an enemy near you:", MenuSkin.Dim);
            var list = new System.Collections.Generic.List<string>(TestWorld.Enemies);
            for (int i = 0; i < list.Count; i += 4)
            {
                var r = f.Row(Ui.RowH, 0, 0, 0, 0);
                for (int j = 0; j < 4 && i + j < list.Count; j++)
                    if (Ui.Button(r[j], PixelFont.Fit(list[i + j], r[j].width - 4))) TestWorld.SpawnEnemy(list[i + j]);
            }
        }

        private float _nextWorldLabel;

        /// <summary>The server browser's World column for this host: the day and chapter its world is on.</summary>
        private static string WorldLabel()
        {
            try
            {
                if (!WorldShare.InWorld) return "";
                int day = Singleton<Controller>.Instance.day;
                int chapter = Singleton<WorldGenerator>.Instance != null ? Singleton<WorldGenerator>.Instance.chapterID : 0;
                return chapter > 0 ? $"Day {day}, chapter {chapter}" : $"Day {day}";
            }
            catch { return ""; }
        }

        private float _nextPresence;
        private string _lastPresence;
        private bool _testPresence;

        /// <summary>Our Steam rich presence: what friends see in "View game info" (see SteamBackend.SetPresence).</summary>
        private void UpdatePresence()
        {
            string text;
            try { text = PresenceText(); }
            catch (Exception e) { text = $"DarkwoodMP {PluginInfo.Version}"; Log.LogWarning($"Steam presence: {e.Message}"); }
            if (text.Length > 256) text = text.Substring(0, 256); // Steam's limit for a value
            bool changed = text != _lastPresence;
            _lastPresence = text;
            if (_steam == null) { if (changed) Log.LogInfo($"[presence] {text} (no Steam: not set)"); return; }
            try { SetSteamPresence(text, changed); }
            catch (Exception e) { Log.LogWarning($"Couldn't set the Steam presence: {e.Message}"); }
        }

        // Non-inlined, like CreateSteamBackend: names SteamBackend, so it runs only once Steam is there.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetSteamPresence(string text, bool log)
        {
            if (!(_steam is SteamBackend sb)) return;
            sb.SetPresence(text, _session.Active ? _session.PlayerCount : 1);
            if (log) Log.LogInfo($"[presence] {text} (Steam has: {sb.PresenceReadBack()})");
        }

        /// <summary>
        /// "DarkwoodMP 0.22.0 · Chapter 2, day 5 · Silent Forest · Hosting, 3/5 players", or "... · In the main menu".
        /// </summary>
        private string PresenceText()
        {
            var parts = new List<string> { $"DarkwoodMP {PluginInfo.Version}" };
            if (!WorldShare.InWorld) { parts.Add("In the main menu"); return string.Join(" · ", parts); }
            int day = Singleton<Controller>.Instance.day;
            int chapter = Singleton<WorldGenerator>.Instance != null ? Singleton<WorldGenerator>.Instance.chapterID : 0;
            parts.Add(chapter > 0 ? $"Chapter {Mathf.Clamp(chapter, 1, 10)}, day {day}" : $"Day {day}");
            string biome = BiomeName();
            if (!string.IsNullOrEmpty(biome)) parts.Add(biome);
            if (!_session.Active) parts.Add("Playing alone");
            else
            {
                string count = $"{_session.PlayerCount}/{_session.Backend.MaxPlayers} players";
                if (_session.IsHost) parts.Add($"Hosting, {count}");
                else if (_session.Names.TryGetValue(_session.HostId, out var host) && !string.IsNullOrEmpty(host)) parts.Add($"In {host}'s game, {count}");
                else parts.Add($"In co-op, {count}");
            }
            return string.Join(" · ", parts);
        }

        private static bool _biomeKeysLogged;

        /// <summary>
        /// The name of the part of the map our player is in, as the game's map screen names it (its "UI" sheet, key
        /// "biome_" + the biome type), or an English name if the sheet can't be read. Empty inside a separate
        /// location (a cave, an interior), where the biome doesn't apply.
        /// </summary>
        private static string BiomeName()
        {
            if (Player.Instance == null || Player.Instance.whereAmI == null) return "";
            if (Singleton<OutsideLocations>.Instance != null && Singleton<OutsideLocations>.Instance.playerInOutsideLocation) return "";
            var b = Player.Instance.whereAmI.currentInfoBiome;
            if (b == null || b.type == Biome.Type.empty) return "";
            // The game's sheet has biome_forest, _forest_mutated, _meadow and _swamp; dense forest is forest on its map.
            string key = "biome_" + (b.type == Biome.Type.forest_dense ? Biome.Type.forest : b.type);
            string name = "";
            try
            {
                if (!_biomeKeysLogged)
                {
                    _biomeKeysLogged = true;
                    var keys = new List<string>();
                    foreach (var k in Language.GetAllKeysForSheet("UI").Keys) if (k.StartsWith("biome")) keys.Add(k);
                    Log.LogInfo($"[presence] the game's biome names: {string.Join(", ", keys)}");
                }
                name = Language.Get(key, "UI");
            }
            catch { }
            if (!string.IsNullOrEmpty(name) && name != key) return name;
            switch (b.type)
            {
                case Biome.Type.meadow: return "Dry Meadow";
                case Biome.Type.forest:
                case Biome.Type.forest_dense: return "Silent Forest";
                case Biome.Type.swamp: return "Swamp";
                default: return "";
            }
        }

        private void OnApplicationQuit() => _characters?.SaveNow();

        /// <summary>A message over your character, as the game shows its own (and in the log).</summary>
        public static void Notice(string text)
        {
            Log.LogInfo($"[notice] {text}");
            try { if (WorldShare.InWorld && Player.Instance != null) Player.Instance.displayMessage(text); }
            catch (Exception e) { Log.LogWarning($"Couldn't show a notice: {e.Message}"); }
        }

        private void OnDestroy()
        {
            _session?.Dispose();
            _harmony?.UnpatchSelf();
        }
    }
}
