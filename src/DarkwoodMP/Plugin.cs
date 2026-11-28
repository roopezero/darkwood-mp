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

    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        public static Plugin Instance { get; private set; }
        public static ModConfig Cfg { get; private set; }
        public static ConfigEntry<int> SendRate => Cfg.SendRate;
        public static ConfigEntry<bool> LogPlayerSprites =>
            Cfg.LogPlayerSprites;

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

        private bool _wasInWorld;

        /// <summary>
        /// Why you can't host yet, or null if you can. A host shares the world
        /// it's in, so hosting starts from inside your world; from the main
        /// menu a server would only turn joiners away.
        /// </summary>
        public static string CantHostReason =>
            Instance._host.Hosting.CantHostReason;

        /// <summary>
        /// Open the co-op panel (Multiplayer in the pause menu).
        /// </summary>
        public void OpenPanel() => _host.Panel.SetPanel(true);

        /// <summary>Open or close the co-op panel.</summary>
        internal void SetPanel(bool show) => _host.Panel.SetPanel(show);

        /// <summary>
        /// The main menu's Join Game: the server browser alone. Once a join
        /// starts the Multiplayer window opens to show how it goes
        /// (DirectPanel.OnJoining); closing the browser before that closes the
        /// co-op windows (Update).
        /// </summary>
        public void OpenJoin() => _host.Panel.OpenJoin();

        /// <summary>The co-op panel is open.</summary>
        public bool PanelOpen => _host.Panel.IsOpen;

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
        internal bool SteamMissing => _host.SteamStart.Missing;

        /// <summary>
        /// Your name (General.PlayerName; empty: the Steam or OS user name),
        /// changed in Options.
        /// </summary>
        public void SetPlayerName(string name) =>
            _host.SteamStart.SetPlayerName(name);

        /// <summary>
        /// The Start Game window's host box: host with Steam or Direct once the
        /// world that's starting has loaded, as Multiplayer in the pause menu
        /// would. Gives up if the game is back at the main menu instead.
        /// </summary>
        internal void HostWhenReady(bool steam) =>
            StartCoroutine(_host.Hosting.WhenWorldReady(steam));


        /// <summary>
        /// The map test steps, set by the DarkwoodMP.MapTests plugin at its
        /// Awake; null when it isn't loaded (a player's game).
        /// </summary>
        internal ITestSteps TestSteps { get; set; }

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
            Cfg = new ModConfig(Config);

            _host = new ModHost(Hosting.HostStart.OpensPort);
            _host.AddPanel(this);
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
            _host.SteamStart.UpdateLocalName();

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
            if (Testing.TestArgRules.Has(args, "--dwmp-profile-load"))
            {
                _host.LoadProfiler.Start(_harmony);
            }

            // "--dwmp-test-control PORT": take test commands from
            // tools/run-test.sh (one game for a whole test run).
            if (Testing.TestArgRules.TryInt(
                args, "--dwmp-test-control", out int controlPort))
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
                MenuWindows.AnyOpen || _host.SkillsPanel.Open
                    || _host.Drag.WindowOpen);
        }

        private void Update()
        {
            if (_host.FpsCap > 0
                && (Application.targetFrameRate != _host.FpsCap
                    || QualitySettings.vSyncCount != 0))
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = _host.FpsCap;
            }

            _host.SteamStart.Update();
            _host.Panel.Update();
            _host.MenuEntry.Update();
            _host.LoadProfiler.Frame();
            _host.Control.Update();
            _host.EpilogueStart.Update();
            _host.Panel.HoldGameButtons();
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
            _host.Presence.Update();

            _host.Join.Update(_host.Session, _host.Share);
            _host.Loading.Update();
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Chapters.Update();
            }

            _host.Progress.Update();
            _host.SkillsPanel.Update(_host.Panel.IsOpen);
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.Update();
            }

            coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.Update();
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

        private void OnGUI()
        {
            _host.Control.OnGUI();
            if (_host.NameTagStyle == null)
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };

                style.normal.textColor = new Color(0.9f, 0.85f, 0.7f);
                _host.NameTagStyle = style;
            }

            _host.Players.DrawNameTags();
            _host.TestArea.DrawLabels();
            CoopSession watching = Coop.Current;
            if (watching != null)
            {
                watching.Spectator.DrawGui();
            }

            _host.Progress.OnGUI();
            _host.SkillsPanel.OnGUI();
            _host.Panel.OnGUI();
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
