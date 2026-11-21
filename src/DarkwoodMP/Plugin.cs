using DarkwoodMP.Menus;
using DarkwoodMP.World.Inventories;
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
using DarkwoodMP.Sync.Players;
using DarkwoodMP.World;
using HarmonyLib;
using Chapters = DarkwoodMP.World.Story.Chapters;
using UnityEngine;

namespace DarkwoodMP
{
    public static class PluginInfo
    {
        public const string Guid = "com.darkwoodmp.coop";
        public const string Name = "DarkwoodMP";
        public const string Version = ModVersion.Text;
    }

    public sealed class ModConfig
    {
        public ConfigEntry<int> SendRate;
        public ConfigEntry<bool> LogPlayerSprites;
        public ConfigEntry<bool> SkipIntro;
        public ConfigEntry<string> PlayerName;
        public ConfigEntry<int> DirectPort;
        public ConfigEntry<bool> OpenPort;
        public ConfigEntry<string> LastAddress;
        public ConfigEntry<string> Password;
        public ConfigEntry<string> ServerName;
        public ConfigEntry<int> MaxPlayers;
        public ConfigEntry<bool> LiveWorld;
        public ConfigEntry<bool> SnapshotOffThread;
        public ConfigEntry<string> NewCharacters;
        public ConfigEntry<KeyCode> SkillsKey;
        public ConfigEntry<string> HostWith;
        public ConfigEntry<int> StackSize;
        public ConfigEntry<bool> SoftPlayerCollision;
    }

    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        public static Plugin Instance { get; private set; }
        public static ModConfig Cfg { get; private set; }
        public static ConfigEntry<int> SendRate => Cfg.SendRate;
        public static ConfigEntry<bool> LogPlayerSprites =>
            Cfg.LogPlayerSprites;
        public static GUIStyle NameTagStyle;

        private Harmony _harmony;
        private ModHost _host;
        /// <summary>
        /// The Direct panel's server browser (the map test's "lan-search" uses
        /// it).
        /// </summary>
        internal static ServerBrowser Browser
        {
            get
            {
                Plugin instance = Instance;
                if (instance != null && instance._host != null)
                {
                    return instance._host.Browser;
                }

                return null;
            }
        }

        private DirectPanel _direct;
        private bool _wasInWorld;
        // the system cursor's visibility before the panel showed it
        private bool? _cursorBefore;
        // SteamManager.s_instance and its m_bInitialized
        private FieldInfo _steamInstance, _steamInitialized;
        private bool _steamUnavailable;
        private float _nextSteamCheck;

        private string _spawnType;
        private bool _showMenu;
        private bool _buttonsOffByUs;

        /// <summary>
        /// Why you can't host yet, or null if you can. A host shares the world
        /// it's in, so hosting starts from inside your world; from the main
        /// menu a server would only turn joiners away.
        /// </summary>
        public static string CantHostReason
        {
            get
            {
                if (!WorldShare.InWorld)
                {
                    return "To host, load your world first (Start Game), then "
                        + "press Esc and choose Multiplayer in the pause menu.";
                }

                // After leaving someone's game you're still in your copy of
                // their world (your own saves are untouched).
                WorldShare share = Plugin.Instance.Host.Share;
                if (share != null && share.PlayingHostCopy)
                {
                    return "This is your copy of another host's world. To "
                        + "host, go back to the main menu and load your own "
                        + "world.";
                }

                if (World.Story.EpilogueSync.InEpilogueAlone())
                {
                    return "This world is in the epilogue, which can't be "
                        + "shared once it has begun. Host from a save before "
                        + "the end of the game.";
                }

                return null;
            }
        }

        /// <summary>
        /// Open the co-op panel (Multiplayer in the pause menu).
        /// </summary>
        public void OpenPanel() => SetPanel(true);

        /// <summary>
        /// The main menu's Join Game: the server browser alone. Once a join
        /// starts the Multiplayer window opens to show how it goes
        /// (DirectPanel.OnJoining); closing the browser before that closes the
        /// co-op windows (Update).
        /// </summary>
        public void OpenJoin()
        {
            _showMenu = true;
            _panel.Open = false;
            _direct.OpenServerBrowser();
        }

        /// <summary>The co-op panel is open.</summary>
        public bool PanelOpen => _showMenu;

        /// <summary>
        /// The process state: the session, its backends, the server browser
        /// (the menus say what leaving the session does).
        /// </summary>
        internal ModHost Host => _host;

        /// <summary>
        /// Steam is running and its backend is up (the Start Game window offers
        /// hosting with it).
        /// </summary>
        internal bool SteamAvailable => _host.Steam is IGuiSection;
        /// <summary>
        /// Steam isn't there at all (GOG), as opposed to not up yet.
        /// </summary>
        internal bool SteamMissing => _steamUnavailable;

        /// <summary>
        /// The Start Game window's host box: host with Steam or Direct once the
        /// world that's starting has loaded, as Multiplayer in the pause menu
        /// would. Gives up if the game is back at the main menu instead.
        /// </summary>
        internal void HostWhenReady(bool steam) =>
            StartCoroutine(HostWhenWorldReady(steam));

        private System.Collections.IEnumerator HostWhenWorldReady(bool steam)
        {
            float t0 = Time.realtimeSinceStartup;
            while (WorldShare.MainMenuReady()
                && Time.realtimeSinceStartup - t0 < 20f)
            {
                yield return null; // the load starts
            }

            while (!WorldShare.WorldReady)
            {
                if (WorldShare.MainMenuReady())
                {
                    Log.LogWarning(
                        "[start game] back at the main menu before the world "
                            + "loaded: not hosting");
                    yield break;
                }

                yield return null;
            }

            if (_host.Session.Active)
            {
                yield break;
            }

            if (CantHostReason is string why)
            {
                Log.LogWarning($"[start game] can't host: {why}");
                yield break;
            }

            if (steam && _host.Steam is IGuiSection steamGui)
            {
                steamGui.Host();
                Log.LogInfo("[start game] hosting with Steam");
            }
            else
            {
                _host.Session.LastRefusal = null;
                bool ok = HostDirect();
                int port = Cfg.DirectPort.Value;
                string status = "started";
                if (!ok)
                {
                    status = _host.Tcp.Status;
                }

                Log.LogInfo(
                    $"[start game] hosting with Direct on port {port}: "
                        + $"{status}");
            }
        }

        /// <summary>
        /// Whether Direct hosting asks the router to forward the port. A test
        /// game never touches the real router (the tests host over and over);
        /// it opens the port only on the fake router of --dwmp-upnp-test.
        /// </summary>
        private static bool OpensPort() =>
            PortMapper.TestRouter != null || (Cfg.OpenPort.Value && !TestMode);

        /// <summary>
        /// Host with Direct on the configured port; a password or player limit
        /// given here is for this time only.
        /// </summary>
        private bool HostDirect(string password = null, int maxPlayers = 0)
        {
            string serverName = Cfg.ServerName.Value;
            int port = Cfg.DirectPort.Value;
            if (password == null)
            {
                password = Cfg.Password.Value;
            }

            if (maxPlayers <= 0)
            {
                maxPlayers = Cfg.MaxPlayers.Value;
            }

            return _host.Tcp.Host(serverName, port, password, maxPlayers);
        }

        internal void SetPanel(bool show)
        {
            _showMenu = show;
            if (show) // in its default place each time it's opened
            {
                _panel.ResetPlace();
                _panel.Show();
            }
            else
            {
                _panel.Open = false;
                _direct.CloseAll();
            }
        }

        /// <summary>
        /// Whether the game was started for the tests (a --dwmp-test...
        /// argument, the test world, or tools/run-test.sh's test control). Only
        /// then does the Multiplayer window offer the test world and its tools.
        /// </summary>
        public static bool TestMode { get; private set; }

        /// <summary>
        /// The map test steps, set by the DarkwoodMP.MapTests plugin at its
        /// Awake; null when it isn't loaded (a player's game).
        /// </summary>
        internal ITestSteps TestSteps { get; set; }

        private UiWindow _panel;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            var problem = CoreCheck.Problem(Info.Location);
            if (problem != null)
            {
                TurnOff(problem);
                return;
            }

            try
            {
                ProbeCore();
            }
            catch (Exception e)
            {
                TurnOff(e.Message);
                return;
            }

            Load();
        }

        // The rest of Awake. Not inlined: compiling Awake must not make the
        // JIT load the core, and this names core types.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Load()
        {
            Cfg = new ModConfig
            {
                PlayerName = Config.Bind(
                    "General", "PlayerName", "",
                    "Name shown to other players. Empty = Steam name, or your "
                        + "OS user name."),
                SendRate = Config.Bind(
                    "Network", "SendRate", 20,
                    "Player state updates per second."),
                ServerName = Config.Bind(
                    "Host", "ServerName", "Darkwood",
                    "Your server's name in other players' server browsers."),
                DirectPort = Config.Bind(
                    "Host", "Port", TcpBackend.DefaultPort,
                    "Port to host on: TCP for the game, UDP for the server "
                        + "browser."),
                OpenPort = Config.Bind(
                    "Host", "OpenPort", true,
                    "When hosting with Direct, ask your router to forward the "
                        + "port (UPnP or NAT-PMP), and close it again when you "
                        + "stop. Off: forward it by hand."),
                Password = Config.Bind(
                    "Host", "Password", "",
                    "Password players need to join your server (empty for "
                        + "none)."),
                MaxPlayers = Config.Bind(
                    "Host", "MaxPlayers", Protocol.MaxPlayers,
                    "Most players at once, you included."),
                LiveWorld = Config.Bind(
                    "Host", "LiveWorld", true,
                    "Players who join get the world as it is now (a snapshot "
                        + "saved for them, your own save untouched). Off: as "
                        + "you last saved it."),
                SnapshotOffThread = Config.Bind(
                    "Host", "SnapshotOffThread", true,
                    "Write the snapshot for a joiner on a worker thread, so "
                        + "your game stops only while the world is read. Off: "
                        + "the game saves it its own way, all at once."),
                NewCharacters = Config.Bind(
                    "Host", "NewCharacters", "fresh",
                    new ConfigDescription(
                        "Someone joining your world for the first time starts "
                            + "with: fresh (no items, full health, beside you) "
                            + "or copy (a copy of your character).",
                        new AcceptableValueList<string>("fresh", "copy"))),
                HostWith = Config.Bind(
                    "Host", "HostWith", "steam",
                    new ConfigDescription(
                        "How the Start Game window hosts: steam (friends join "
                            + "by invite) or direct (IP and port). Without "
                            + "Steam (GOG) it's always direct.",
                        new AcceptableValueList<string>("steam", "direct"))),
                StackSize = Config.Bind(
                    "Host", "StackSize", 50,
                    new ConfigDescription(
                        "How many of an item one inventory slot holds, for "
                            + "every item that stacks (players in your game "
                            + "use yours). Items that already stack higher "
                            + "keep theirs. 0: the game's own sizes.",
                        new AcceptableValueRange<int>(0, 999))),
                LastAddress = Config.Bind(
                    "Network", "LastAddress", "",
                    "Last server joined directly."),
                SkipIntro = Config.Bind(
                    "General", "SkipIntro", true,
                    "Start without the studio logo (about 6 seconds sooner at "
                        + "the main menu)."),
                SkillsKey = Config.Bind(
                    "General", "SkillsKey", KeyCode.K,
                    "Key that opens and closes the Skills panel (spend skill "
                        + "points)."),
                SoftPlayerCollision = Config.Bind(
                    "General", "SoftPlayerCollision", true,
                    "Players standing in each other are eased apart, gently "
                        + "(no hard bump). Off: players pass through each "
                        + "other."),
                LogPlayerSprites = Config.Bind(
                    "Debug", "LogPlayerSprites", false,
                    "Log every sprite in the player hierarchy once (which are "
                        + "synced/skipped)."),
            };

            _host = new ModHost(OpensPort);
            _direct = new DirectPanel(_host.Tcp, _host.Browser, _host.Session);
            _direct.OnJoining = () =>
            {
                if (!_panel.Open)
                {
                    _panel.ResetPlace();
                    _panel.Show();
                }
            };

            _panel = new UiWindow(0x0D3A, "Multiplayer")
            {
                MinSize = new Vector2(300, 160),
                FitContent = true,
                // Beside the main menu's words (which MenuEntry moves a little
                // to the left), on the right.
                DefaultRect = () => UiWindow.RightSide(
                    380 * PixelFont.Scale, 300 * PixelFont.Scale, top: 0.16f),
                Body = DrawMenu,
                Footer = r =>
                {
                    if (Ui.Button(Ui.Split(r, 0, 90)[1], "Close"))
                    {
                        SetPanel(false);
                    }
                },
                FooterHeight = 13 + 8,
                OnClose = () => SetPanel(false),
            };

            _host.AddWindows();
            _host.AddPlayers();
            _host.Session.OnPlayerJoined += n => Notice($"{n} joined");
            _host.Session.OnPlayerLeft += n => Notice($"{n} left");
            _host.Session.OnPlayerJoining += n => Notice($"{n} is joining...");
            _host.Session.OnPlayerStoppedJoining += n => Notice(
                $"{n} stopped joining");
            _host.Players.OnRemoteDied += n => Notice($"{n} died");
            _host.AddShare(this, Paths.CachePath);
            WorldActions.Register(_host.Session);
            UpdateLocalName();

            _harmony = new Harmony(PluginInfo.Guid);
            PatchAll();
            Log.LogInfo(
                $"{PluginInfo.Name} {PluginInfo.Version} loaded. Multiplayer "
                    + "is in the main menu and the pause menu.");
            // The game skips its logo when it comes back to the menu from a
            // world; set the same flag before the first menu.
            if (Cfg.SkipIntro.Value)
            {
                Core.dontPlayIntroLogos = true;
            }

            StartCoroutine(LogStartTimes());

            var args = Environment.GetCommandLineArgs();
            // "--dwmp-profile-load": time the game's loading code and log what
            // was slowest once a world is ready.
            if (Array.IndexOf(args, "--dwmp-profile-load") >= 0)
            {
                _host.LoadProfiler.Start(_harmony);
            }

            // "--dwmp-test-control PORT": take test commands from
            // tools/run-test.sh (one game for a whole test run).
            int tc = Array.IndexOf(args, "--dwmp-test-control");
            if (tc >= 0 && tc + 1 < args.Length
                && int.TryParse(args[tc + 1], out int controlPort))
            {
                _host.Control.Start(this, controlPort);
            }

            ApplyTestArgs(args);
        }

        /// <summary>
        /// Patch class by class, so one that doesn't fit this build of the game
        /// (a GOG build that differs, a game update) is reported and skipped
        /// instead of stopping every patch after it.
        /// </summary>
        private void PatchAll()
        {
            int ok = 0, failed = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(
                typeof(Plugin).Assembly))
            {
                if (type
                    .GetCustomAttributes(typeof(HarmonyPatch), false).Length
                        == 0)
                {
                    continue;
                }

                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    Exception innerException = e.InnerException;
                    string message = null;
                    if (innerException != null)
                    {
                        message = innerException.Message;
                    }

                    if (message == null)
                    {
                        message = e.Message;
                    }

                    Log.LogError(
                        $"Patch {type.Name} doesn't fit this build of the "
                            + $"game, skipped: {message}");
                }
            }

            if (failed > 0)
            {
                Log.LogWarning(
                    $"{failed} of {ok + failed} patches skipped; what they do "
                        + "won't be shared");
            }
            else
            {
                Log.LogInfo($"All {ok} patches applied");
            }
        }

        /// <summary>
        /// After the frame's game code: what the player carries now (for
        /// undoing a refused request's change), and the mouse confined only
        /// while playing.
        /// </summary>
        private void LateUpdate()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Authority.Inventory.LateUpdate();
            }

            _host.Join.LateUpdate();
            coop = Coop.Current;
            if (coop != null)
            {
                coop.View.LateUpdate(_host.Session.Active);
            }

            CursorLock.LateUpdate(MenuWindows.AnyOpen);
            _host.Cursor.LateUpdate(
                MenuWindows.AnyOpen || SkillsPanel.Open
                    || _host.Drag.WindowOpen);
        }

        private static int _fpsCap;

        private void Update()
        {
            if (_fpsCap > 0
                && (Application.targetFrameRate != _fpsCap
                    || QualitySettings.vSyncCount != 0))
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = _fpsCap;
            }

            if (_host.Steam == null && !_steamUnavailable
                && Time.unscaledTime >= _nextSteamCheck)
            {
                _nextSteamCheck = Time.unscaledTime + 1f;
                if (SteamReady())
                {
                    TryCreateSteamBackend();
                }
            }

            // Escape closes the panel, unless a question over it takes it
            // (ConfirmDialog answers No).
            if (_showMenu && !_host.Confirm.Open
                && Input.GetKeyDown(KeyCode.Escape))
            {
                SetPanel(false);
            }

            // The browser was opened alone (Join Game) and closed without
            // joining: nothing of the co-op windows is left.
            if (_showMenu && !_panel.Open && !_direct.AnyOpen)
            {
                SetPanel(false);
            }

            _host.MenuEntry.Update();
            _host.LoadProfiler.Frame();
            _host.Control.Update();
            // Over the game's menus, its buttons (and its Escape) would take
            // the clicks and keys meant for our windows.
            bool windows = MenuWindows.AnyOpen;
            if (_buttonsOffByUs && !windows)
            {
                _buttonsOffByUs = false;
                if (Singleton<Controller>.Instance != null)
                {
                    Singleton<Controller>.Instance.buttonsDisabled = false;
                }
            }

            if (windows && Core.mainMenu
                && Singleton<Controller>.Instance != null
                && !Singleton<Controller>.Instance.buttonsDisabled)
            {
                Singleton<Controller>.Instance.buttonsDisabled = true;
                _buttonsOffByUs = true;
            }

            // After the buttons are let go: an answered question's action, then
            // the menu's list (and a click on it).
            _host.Confirm.Update();
            _host.Options.Update();
            _host.MenuList.Update();
            _host.Profiler.Begin();
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.WatchWorld();
            }

            _host.Session.Update();
            _host.Profiler.Mark("session");
            _host.Browser.Update();
            _host.Profiler.Mark("browser");
            _host.Players.Update();
            _host.Profiler.Mark("players");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.Update();
            }

            _host.Profiler.Mark("player fx");
            _host.Proxies.Update(_host.Players, _host.Session.IsHost);
            _host.Profiler.Mark("proxies");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Enemies.Update();
            }

            _host.Profiler.Mark("enemies");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Time.Update();
            }

            _host.Profiler.Mark("time");
            // also without co-op: each upgrade adds a row to the storage
            _host.Workbenches.Update();
            // also without co-op: see Containers.EachFrame
            Containers.EachFrame();
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Containers.Update();
            }

            _host.Profiler.Mark("containers");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Characters.Update();
            }

            _host.Profiler.Mark("characters");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Story.Update();
            }

            _host.Profiler.Mark("flags");
            // also without co-op: the flag is kept for when others join
            _host.Tank.Update();
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Drags.Update();
            }

            _host.Profiler.Mark("drag");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Pushes.Update();
            }

            _host.Profiler.Mark("door push");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.Update();
            }

            _host.Profiler.Mark("fuel");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.WorldEvents.Update();
            }

            _host.Profiler.Mark("events");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.HostIds.Update();
            }

            _host.Profiler.Mark("host ids");
            if (coop != null)
            {
                coop.Nights.Update();
            }

            _host.Profiler.Mark("night objects");
            coop = Coop.Current;
            if (coop != null)
            {
                coop.EnemyFx.Update();
            }

            _host.Profiler.Mark("enemy fx");
            _host.Share.Update();
            _host.Profiler.Mark("world");
            if (_host.Tcp.IsHost && Time.unscaledTime >= _nextWorldLabel)
            {
                _nextWorldLabel = Time.unscaledTime + 2f;
                _host.Tcp.WorldLabel = WorldLabel();
            }

            if ((_host.Steam != null || _testPresence)
                && Time.unscaledTime >= _nextPresence)
            {
                _nextPresence = Time.unscaledTime + 5f;
                UpdatePresence();
            }

            _host.Join.Update(_host.Session, _host.Share);
            _host.Loading.Update();
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Chapters.Update();
            }

            _host.Progress.Update();
            SkillsPanel.Update(_showMenu);
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.Update();
            }

            coop = Coop.Current;
            if (coop != null)
            {
                coop.Prologue.Update();
            }

            coop = Coop.Current;
            if (coop != null)
            {
                coop.Locations.Update();
            }

            coop = Coop.Current;
            if (coop != null)
            {
                coop.Views.Update();
                coop.Infections.Update();
            }

            coop = Coop.Current;
            if (coop != null)
            {
                coop.Pause.Update();
            }

            _host.Profiler.Mark("pause");
            if (coop != null)
            {
                coop.Mornings.Update();
            }

            coop = Coop.Current;
            if (_host.Session.IsHost && WorldShare.InWorld && coop != null)
            {
                coop.Traders.HostUpdate();
            }

            coop = Coop.Current;
            if (coop != null)
            {
                coop.Npcs.Update();
            }

            _host.Profiler.Mark("npcs");
            // also alone: a dialogue's NPC can go away then too
            _host.Dialogues.Update();

            _host.TestArea.Update();
            if (WorldShare.InWorld && !_wasInWorld) // a new world
            {
                coop = Coop.Current;
                if (coop != null)
                {
                    coop.Spectator.Reset();
                }

                _host.Spawns.Clear();
            }

            _wasInWorld = WorldShare.InWorld;
            _host.Profiler.EndFrame();
        }

        /// <summary>
        /// Checks the game's own SteamManager via reflection (it may be missing
        /// or never init on GOG). Only one the game made is looked at:
        /// SteamManager.Initialized makes a SteamManager when there's none yet,
        /// and its Awake quits the game when steam_api can't be loaded (as on a
        /// GOG build that never makes one itself).
        /// </summary>
        private bool SteamReady()
        {
            try
            {
                if (_steamInstance == null)
                {
                    var t = AccessTools.TypeByName("SteamManager");
                    if (t != null)
                    {
                        _steamInstance = t.GetField(
                            "s_instance",
                            BindingFlags.NonPublic | BindingFlags.Public
                                | BindingFlags.Static);
                    }
                    else
                    {
                        _steamInstance = null;
                    }

                    if (t != null)
                    {
                        _steamInitialized = t.GetField(
                            "m_bInitialized",
                            BindingFlags.NonPublic | BindingFlags.Public
                                | BindingFlags.Instance);
                    }
                    else
                    {
                        _steamInitialized = null;
                    }

                    if (_steamInstance == null || _steamInitialized == null)
                    {
                        _steamUnavailable = true;
                        Log.LogInfo(
                            "No SteamManager in this build: direct IP only");
                        return false;
                    }
                }

                var manager =
                    _steamInstance.GetValue(null) as UnityEngine.Object;
                return manager != null
                    && (bool)_steamInitialized.GetValue(manager);
            }
            catch
            {
                return false; // Steam not running (GOG)
            }
        }

        private void TryCreateSteamBackend()
        {
            try
            {
                CreateSteamBackend();
            }
            catch (Exception e)
            {
                _steamUnavailable = true;
                Log.LogWarning(
                    "Steam networking unavailable, direct IP only: "
                        + $"{e.Message}");
            }
        }

        // Separate, non-inlined method: if Steamworks.NET can't load, only this
        // call fails.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateSteamBackend()
        {
            _host.AddSteam(new SteamBackend());
            UpdateLocalName();
            Log.LogInfo("Steam ready: Steam lobbies and invites enabled");
        }

        /// <summary>
        /// Your name (General.PlayerName; empty: the Steam or OS user name),
        /// changed in Options.
        /// </summary>
        public void SetPlayerName(string name)
        {
            string stored = name;
            if (stored == null)
            {
                stored = "";
            }

            Cfg.PlayerName.Value = stored;
            UpdateLocalName();
        }

        private void UpdateLocalName()
        {
            string name = Cfg.PlayerName.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                IGuiSection guiSection = _host.Steam as IGuiSection;
                if (guiSection != null)
                {
                    name = guiSection.SuggestedPlayerName;
                }
                else
                {
                    name = null;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Environment.UserName;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                _host.Session.LocalName = "Player";
            }
            else
            {
                _host.Session.LocalName = name.Trim();
            }
        }

        private void OnGUI()
        {
            _host.Control.OnGUI();
            if (NameTagStyle == null)
            {
                NameTagStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };

                NameTagStyle.normal.textColor = new Color(0.9f, 0.85f, 0.7f);
            }

            _host.Players.DrawNameTags();
            _host.TestArea.DrawLabels();
            CoopSession watching = Coop.Current;
            if (watching != null)
            {
                watching.Spectator.DrawGui();
            }

            _host.Progress.OnGUI();
            SkillsPanel.OnGUI();
            ShowSystemCursor(
                MenuWindows.AnyOpen || SkillsPanel.Open
                    || _host.Drag.WindowOpen);
            if (!_showMenu && !_host.MenuList.Shown && !MenuWindows.AnyOpen)
            {
                _host.Drag.OnGUI();
                return;
            }

            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            // The menu's list under the windows, then the co-op windows, the
            // other menu windows, and a question on top.
            _host.MenuList.OnGUI();
            if (_showMenu)
            {
                _panel.Title =
                    $"Multiplayer  -  DarkwoodMP {PluginInfo.Version}";
                _panel.OnGUI();
                _direct.OnGUI();
            }

            MenuWindows.DrawAll();
            _host.Confirm.OnGUI();
            GUI.skin = skin;
            _host.Drag.OnGUI();
        }

        /// <summary>
        /// The game draws its cursor as a sprite in the world's UI, which the
        /// panel covers. While the panel is open the system cursor is shown
        /// over it; when it closes, the cursor is as the game had it.
        /// </summary>
        private void ShowSystemCursor(bool show)
        {
            if (show)
            {
                if (_cursorBefore == null)
                {
                    _cursorBefore = UnityEngine.Cursor.visible;
                }

                if (!UnityEngine.Cursor.visible)
                {
                    UnityEngine.Cursor.visible = true;
                }
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
                // The name is changed in Options (Multiplayer); here it's only
                // shown.
                var r = f.Row(Ui.RowH, 90, 0, 0);
                Ui.Label(r[0], "Your name");
                Ui.Label(
                    r[1], PixelFont.Fit(_host.Session.LocalName, r[1].width),
                    MenuSkin.Bright);
                if (!_host.Session.Active)
                {
                    Ui.Label(
                        r[2], "change it in Options", MenuSkin.Dim,
                        TextAnchor.MiddleRight);
                }
            }

            if (_host.Share.Progress is var p && p.HasValue)
            {
                Ui.Text(
                    f,
                    "Downloading the host's world: "
                        + $"{p.Value.Got / 1048576f:0.0} / "
                        + $"{p.Value.Total / 1048576f:0.0} MB",
                    MenuSkin.Bright);
            }
            else if (_host.Share.LoadStatus != null)
            {
                Ui.Text(f, _host.Share.LoadStatus, MenuSkin.Bright);
            }

            if (TestWorld.Active)
            {
                Ui.Text(
                    f,
                    "You're in the test world: your own saves are untouched.",
                    MenuSkin.Dim);
            }
            else if (SaveRedirect.Active)
            {
                Ui.Text(
                    f,
                    "You're in the host's world: your own saves are untouched.",
                    MenuSkin.Dim);
            }
            else if (WorldShare.InWorld && !_host.Session.Active)
            {
                Ui.Text(
                    f, "Joining a game leaves the one you're in (save first).",
                    MenuSkin.Dim);
            }

            if (!_host.Session.Active && CantHostReason is string why)
            {
                Ui.Text(f, why, MenuSkin.Dim);
            }

            Ui.Heading(f, "Steam");
            if (_host.Steam is IGuiSection steamGui)
            {
                steamGui.DrawGui(_host.Session, f);
            }
            else
            {
                string text = "Waiting for Steam...";
                if (_steamUnavailable)
                {
                    text =
                        "Steam isn't running (the GOG version, say): use a "
                            + "direct connection.";
                }

                Ui.Text(f, text, MenuSkin.Dim);
            }

            Ui.Heading(f, "Direct connection");
            _direct.Draw(f);

            if (_host.Session.Active && _host.Session.Names.Count > 0)
            {
                Ui.Heading(f, "Players");
                ulong hostId;
                if (_host.Session.IsHost)
                {
                    hostId = _host.Session.LocalId;
                }
                else
                {
                    hostId = _host.Session.HostId;
                }

                foreach (var kv in _host.Session.Names)
                {
                    string hostTag = "";
                    if (kv.Key == hostId)
                    {
                        hostTag = "  (host)";
                    }

                    string youTag = "";
                    if (kv.Key == _host.Session.LocalId)
                    {
                        youTag = "  (you)";
                    }

                    string tags = hostTag + youTag;
                    Ui.Label(f, kv.Value + tags);
                }
            }

            if (TestMode)
            {
                DrawTestWorld(f);
            }
        }

        /// <summary>
        /// The test world (only when the game was started for the tests): start
        /// or reset it from the main menu; in it, the host's tools.
        /// </summary>
        private void DrawTestWorld(Flow f)
        {
            bool inMenu = WorldShare.MainMenuReady() && !_host.Session.Active;
            if (!inMenu && !(TestWorld.Active && !_host.Session.IsClient))
            {
                return;
            }

            Ui.Heading(f, "Test world");
            if (inMenu)
            {
                Ui.Text(
                    f,
                    "A copy of your saves with a test area of everything "
                        + "that's shared. Host it, and friends join it as "
                        + "usual.",
                    MenuSkin.Dim);
                var r = f.Row(Ui.RowH, 0, 90);
                string label = "Make and start a test world";
                if (TestWorld.Exists)
                {
                    label = "Start the test world";
                }

                if (Ui.Button(r[0], label))
                {
                    SetPanel(false);
                    StartCoroutine(
                        _host.TestArea.Start(_host.Share, reset: false));
                }

                if (TestWorld.Exists && Ui.Button(r[1], "Reset it"))
                {
                    SetPanel(false);
                    StartCoroutine(
                        _host.TestArea.Start(_host.Share, reset: true));
                }

                if (_host.TestArea.Status != null)
                {
                    Ui.Text(f, _host.TestArea.Status);
                }

                return;
            }

            var t = f.Row(Ui.RowH, 0, 0, 0, 0, 0);
            if (Ui.Button(t[0], "Kit"))
            {
                TestWorld.GiveKit();
            }

            if (Ui.Button(t[1], "Dawn"))
            {
                TestWorld.SetTime(Singleton<Controller>.Instance.dayTime + 5);
            }

            if (Ui.Button(t[2], "Noon"))
            {
                TestWorld.SetTime(720);
            }

            if (Ui.Button(t[3], "Dusk"))
            {
                TestWorld.SetTime(
                    Singleton<Controller>.Instance.nightTime - 30);
            }

            if (Ui.Button(t[4], "Night"))
            {
                TestWorld.SetTime(
                    Singleton<Controller>.Instance.nightTime + 30);
            }

            Ui.Label(f, "Spawn an enemy near you:", MenuSkin.Dim);
            var list = new List<string>(TestWorld.Enemies);
            for (int i = 0; i < list.Count; i += 4)
            {
                var r = f.Row(Ui.RowH, 0, 0, 0, 0);
                for (int j = 0; j < 4 && i + j < list.Count; j++)
                {
                    if (Ui.Button(
                        r[j], PixelFont.Fit(list[i + j], r[j].width - 4)))
                    {
                        TestWorld.SpawnEnemy(list[i + j]);
                    }
                }
            }
        }

        private float _nextWorldLabel;

        /// <summary>
        /// The server browser's World column for this host: the day and chapter
        /// its world is on.
        /// </summary>
        private static string WorldLabel()
        {
            try
            {
                if (!WorldShare.InWorld)
                {
                    return "";
                }

                var (day, chapter) = DayAndChapter();
                if (chapter > 0)
                {
                    return $"Day {day}, chapter {chapter}";
                }

                return $"Day {day}";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// The world's day, and its chapter (0 when there's no world
        /// generator).
        /// </summary>
        private static (int day, int chapter) DayAndChapter()
        {
            int day = Singleton<Controller>.Instance.day;
            int chapter = 0;
            if (Singleton<WorldGenerator>.Instance != null)
            {
                chapter = Singleton<WorldGenerator>.Instance.chapterID;
            }

            return (day, chapter);
        }

        private float _nextPresence;
        private string _lastPresence;
        private bool _testPresence;

        /// <summary>
        /// Our Steam rich presence: what friends see in "View game info" (see
        /// SteamBackend.SetPresence).
        /// </summary>
        private void UpdatePresence()
        {
            string text;
            try
            {
                text = PresenceText();
            }
            catch (Exception e)
            {
                text = $"DarkwoodMP {PluginInfo.Version}";
                Log.LogWarning($"Steam presence: {e.Message}");
            }

            if (text.Length > 256)
            {
                text = text.Substring(0, 256); // Steam's limit for a value
            }

            bool changed = text != _lastPresence;
            _lastPresence = text;
            if (_host.Steam == null)
            {
                if (changed)
                {
                    Log.LogInfo($"[presence] {text} (no Steam: not set)");
                }

                return;
            }

            try
            {
                SetSteamPresence(text, changed);
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't set the Steam presence: {e.Message}");
            }
        }

        // Non-inlined, like CreateSteamBackend: names SteamBackend, so it runs
        // only once Steam is there.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetSteamPresence(string text, bool log)
        {
            if (!(_host.Steam is SteamBackend sb))
            {
                return;
            }

            int players = 1;
            if (_host.Session.Active)
            {
                players = _host.Session.PlayerCount;
            }

            sb.SetPresence(text, players);
            if (log)
            {
                Log.LogInfo(
                    $"[presence] {text} (Steam has: {sb.PresenceReadBack()})");
            }
        }

        /// <summary>
        /// "DarkwoodMP 0.22.0 · Chapter 2, day 5 · Silent Forest · Hosting, 3/5
        /// players", or "... · In the main menu".
        /// </summary>
        private string PresenceText()
        {
            var parts = new List<string> { $"DarkwoodMP {PluginInfo.Version}" };
            if (!WorldShare.InWorld)
            {
                parts.Add("In the main menu");
                return string.Join(" · ", parts);
            }

            var (day, chapter) = DayAndChapter();
            string item;
            if (chapter > 0)
            {
                item = $"Chapter {Mathf.Clamp(chapter, 1, 10)}, day {day}";
            }
            else
            {
                item = $"Day {day}";
            }

            parts.Add(item);
            string biome = BiomeName();
            if (!string.IsNullOrEmpty(biome))
            {
                parts.Add(biome);
            }

            if (!_host.Session.Active)
            {
                parts.Add("Playing alone");
            }
            else
            {
                string count =
                    $"{_host.Session.PlayerCount}/"
                        + $"{_host.Session.Backend.MaxPlayers} players";
                if (_host.Session.IsHost)
                {
                    parts.Add($"Hosting, {count}");
                }
                else if (_host.Session.Names.TryGetValue(
                    _host.Session.HostId, out var host)
                    && !string.IsNullOrEmpty(host))
                {
                    parts.Add($"In {host}'s game, {count}");
                }
                else
                {
                    parts.Add($"In co-op, {count}");
                }
            }

            return string.Join(" · ", parts);
        }

        private static bool _biomeKeysLogged;

        /// <summary>
        /// The name of the part of the map our player is in, as the game's map
        /// screen names it (its "UI" sheet, key "biome_" + the biome type), or
        /// an English name if the sheet can't be read. Empty inside a separate
        /// location (a cave, an interior), where the biome doesn't apply.
        /// </summary>
        private static string BiomeName()
        {
            if (Player.Instance == null || Player.Instance.whereAmI == null)
            {
                return "";
            }

            if (Singleton<OutsideLocations>.Instance != null
                && Singleton<OutsideLocations>.Instance.playerInOutsideLocation)
            {
                return "";
            }

            var b = Player.Instance.whereAmI.currentInfoBiome;
            if (b == null || b.type == Biome.Type.empty)
            {
                return "";
            }

            // The game's sheet has biome_forest, _forest_mutated, _meadow and
            // _swamp; dense forest is forest on its map.
            Biome.Type biomeType;
            if (b.type == Biome.Type.forest_dense)
            {
                biomeType = Biome.Type.forest;
            }
            else
            {
                biomeType = b.type;
            }

            string key = "biome_" + biomeType;
            string name = "";
            try
            {
                if (!_biomeKeysLogged)
                {
                    _biomeKeysLogged = true;
                    var keys = new List<string>();
                    foreach (var k in Language.GetAllKeysForSheet("UI").Keys)
                    {
                        if (k.StartsWith("biome"))
                        {
                            keys.Add(k);
                        }
                    }

                    Log.LogInfo(
                        "[presence] the game's biome names: "
                            + $"{string.Join(", ", keys)}");
                }

                name = Language.Get(key, "UI");
            }
            catch
            {
            }

            if (!string.IsNullOrEmpty(name) && name != key)
            {
                return name;
            }

            switch (b.type)
            {
                case Biome.Type.meadow:
                    return "Dry Meadow";
                case Biome.Type.forest:
                case Biome.Type.forest_dense:
                    return "Silent Forest";
                case Biome.Type.swamp:
                    return "Swamp";
                default:
                    return "";
            }
        }

        private void OnApplicationQuit()
        {
            // No host when the core didn't load (TurnOff).
            ModHost host = _host;
            if (host != null)
            {
                host.OptionsTest.RestoreIfCutShort();
            }

            // Our character goes to the host here, while the world is whole,
            // and only here: OnDestroy's Dispose leaves the session (which
            // waits for the TCP writers) without sending it again.
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Characters.SaveForQuit();
            }
        }

        /// <summary>
        /// A message over your character, as the game shows its own (and in the
        /// log).
        /// </summary>
        public static void Notice(string text)
        {
            Log.LogInfo($"[notice] {text}");
            try
            {
                if (WorldShare.InWorld && Player.Instance != null)
                {
                    Player.Instance.displayMessage(text);
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't show a notice: {e.Message}");
            }
        }

        // Not inlined: compiling Awake must not make the JIT load the core.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ProbeCore()
        {
            Log.LogInfo($"Core: {Verdict.Allow}");
        }

        // Update and OnGUI use what the rest of Awake sets up, and would
        // throw each frame.
        private void TurnOff(string problem)
        {
            Logger.LogFatal($"DarkwoodMP can't start: {problem}. "
                    + "Extract the whole zip over the game folder again.");
            enabled = false;
        }

        private void OnDestroy()
        {
            ModHost host = _host;
            if (host != null)
            {
                host.Dispose();
            }

            Harmony harmony = _harmony;
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }
    }
}

