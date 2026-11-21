using DarkwoodMP.Testing;
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
    public sealed partial class Plugin
    {
        private readonly List<Coroutine> _tests = new List<Coroutine>();
        private void StartTest(System.Collections.IEnumerator routine) =>
            _tests.Add(StartCoroutine(routine));

        /// <summary>
        /// The --dwmp-* arguments for hosting, joining and testing: from the
        /// command line at start, or from TestControl before each test of a
        /// run.
        /// </summary>
        internal void ApplyTestArgs(string[] args)
        {
            string Arg(string name) => TestArgRules.Arg(args, name);
            bool Has(string name) => TestArgRules.Has(args, name);
            if (TestArgRules.IsTestGame(args))
            {
                _host.TestMode = true;
            }

            _host.Loading.ShotsDir = Arg("--dwmp-load-shots");
            // "--dwmp-window WxH": play in a window of that size (to check the
            // UI at other sizes); without it, the size from before is put back.
            {
                if (TestArgRules.TrySize(
                    Arg("--dwmp-window"), out int ww, out int hh))
                {
                    if (_host.WindowBefore == null)
                    {
                        _host.WindowBefore = (Screen.width, Screen.height,
                            Screen.fullScreenMode);
                    }

                    Screen.SetResolution(ww, hh, FullScreenMode.Windowed);
                    Log.LogInfo($"--dwmp-window: {ww}x{hh}");
                }
                else if (_host.WindowBefore is var sb && sb.HasValue)
                {
                    Screen.SetResolution(sb.Value.w, sb.Value.h, sb.Value.mode);
                    _host.WindowBefore = null;
                }
            }

            // "--dwmp-low-quality": Unity's lowest quality level;
            // "--dwmp-fps N": at most N frames a second (vsync off). For the
            // map4 test, where five games share one machine. Only set, never
            // put back (a test-only game).
            // The game sets vsync from its settings as it starts (Core), so
            // Update keeps the cap in place.
            // "--dwmp-test-presence": log the Steam presence text every 5 s
            // even without Steam, to check it.
            if (Has("--dwmp-test-presence"))
            {
                _host.Presence.Logged = true;
            }

            if (Has("--dwmp-low-quality"))
            {
                QualitySettings.SetQualityLevel(0, true);
                Log.LogInfo("--dwmp-low-quality: quality level 0");
            }

            if (TestArgRules.TryInt(args, "--dwmp-fps", out int fps)
                && fps > 0)
            {
                _host.FpsCap = fps;
                Log.LogInfo($"--dwmp-fps: {fps}");
            }

            bool After(string name, out float secs)
            {
                bool ok = TestArgRules.TrySeconds(args, name, out var given);
                secs = given.Value;
                return ok;
            }


            // "--dwmp-host": start hosting directly at once (testing /
            // always-on hosts); "--dwmp-password P" for this time only.
            // "--dwmp-max-players N" (tests only): this many players, even
            // beyond the mod's 4 (map4 with four clients).
            // "--dwmp-upnp-test HOST:PORT" (tests only): the router is
            // tools/tests/fakerouter.py there; nothing is discovered. Set for
            // each test of a run, so the tests after this one don't keep it.
            PortMapper.TestRouter = Arg("--dwmp-upnp-test");
            if (PortMapper.TestRouter != null)
            {
                Log.LogInfo(
                    $"--dwmp-upnp-test: the router is {PortMapper.TestRouter}");
            }

            if (Has("--dwmp-host"))
            {
                if (TestArgRules.TryInt(args, "--dwmp-max-players", out int mp))
                {
                    TcpBackend.TestMaxPlayers = mp;
                }
                else
                {
                    TcpBackend.TestMaxPlayers = 0;
                }

                _host.Hosting.Direct(
                    Arg("--dwmp-password"), TcpBackend.TestMaxPlayers);
            }

            // "--dwmp-continue N": load your own profile N once the main menu
            // is up; "--dwmp-sandbox DIR": a copy of the saves in DIR.
            if (TestArgRules.TryInt(args, "--dwmp-continue", out int profileId))
            {
                StartTest(
                    _host.Share.ContinueOwnProfile(
                        profileId, Arg("--dwmp-sandbox")));
            }

            _host.Threats.Debug = Has("--dwmp-debug-threat");
            _host.Profiler.Enabled = Has("--dwmp-profile-mod");
            _host.LoadSpeed.Verify = Has("--dwmp-verify-load");
            _host.DebugWorld = Has("--dwmp-debug-world");
            if (Has("--dwmp-test-world"))
            {
                StartTest(WorldTest.Run());
            }

            // "--dwmp-test-client-actions": as a client, use up a rope and open
            // a door; the scripted host refuses it.
            if (Has("--dwmp-test-client-actions"))
            {
                StartTest(WorldTest.ClientActions());
            }

            // "--dwmp-test-client-menu": as a client, once our character has
            // come, go back to the main menu as the pause menu does (the
            // session must end), then join the same server again from the
            // Multiplayer window.
            if (Has("--dwmp-test-client-menu"))
            {
                StartTest(ClientMenuRejoin(Arg("--dwmp-join")));
            }

            // "--dwmp-steam-test": host a Steam lobby once Steam is up, log it,
            // leave (the most one account can test).
            if (Has("--dwmp-steam-test"))
            {
                StartTest(SteamSelfTest());
            }

            // "--dwmp-test-reload N": N seconds after a player joins, your game
            // loads its world again (players should follow).
            if (After("--dwmp-test-reload", out float reloadAfter))
            {
                StartTest(ReloadAfterJoin(reloadAfter));
            }

            // "--dwmp-test-death": 6 seconds after a player joins, your
            // character dies (for testing death with others in the world).
            if (Has("--dwmp-test-death"))
            {
                StartTest(DieAfterJoin());
            }

            // "--dwmp-test-fx": 4 seconds after a player joins, your character
            // is hit for 1 (blood, a pain sound) and burns for 3 seconds
            // without damage, so they see and hear it on your puppet.
            if (Has("--dwmp-test-fx"))
            {
                StartTest(FxAfterJoin());
            }

            // "--dwmp-test-night": once a player is in, send a night worm and a
            // night attacker at them (logged "[night test]").
            if (Has("--dwmp-test-night"))
            {
                StartTest(Combat.NightSpawns.TestAfterJoin());
            }

            // "--dwmp-test-menu N": N seconds after a player joins, your game
            // goes back to the main menu (the server should stop).
            if (After("--dwmp-test-menu", out float menuAfter))
            {
                StartTest(MenuAfterJoin(menuAfter));
            }

            // "--dwmp-test-chapter": once your world is ready, log the triggers
            // in your world that start a chapter ("[chapter test] trigger KIND
            // ID INDEX at X Z"), for a scripted player to ask for.
            if (Has("--dwmp-test-chapter"))
            {
                StartTest(ChapterTriggers());
            }

            // "--dwmp-test-end N": N seconds after a player joins, your game
            // starts the epilogue (the game's end).
            if (After("--dwmp-test-end", out float endAfter))
            {
                StartTest(EndAfterJoin(endAfter));
            }

            // "--dwmp-test-quick-end": as a joiner, skip the epilogue and the
            // credits soon after they start.
            _host.TestQuickEnd = Has("--dwmp-test-quick-end");
            // "--dwmp-test-save N": N seconds after a player joins, the host's
            // game saves (as at dawn), writing their characters.
            if (After("--dwmp-test-save", out float saveAfter))
            {
                StartTest(SaveAfterJoin(saveAfter));
            }

            _host.SpawnType = Arg("--dwmp-spawn");
            // "--dwmp-test-blast": once a player joins, an enemy bomb, damage
            // aura and flame go off at them (see Combat.AreaHitsTest).
            if (Has("--dwmp-test-blast"))
            {
                StartTest(AreaHitsTest.Run());
                StartTest(EnemyFxTest.Run());
            }

            // "--dwmp-test-away" (with --dwmp-dummy): knock an object near you,
            // then be away (as in a separate location) for a few seconds; logs
            // the push sent and the enemies kept running near the dummy.
            if (Has("--dwmp-test-away"))
            {
                StartTest(World.AwayTest.Run());
            }

            // "--dwmp-join host:port": join a server once the main menu is up.
            if (Arg("--dwmp-join") is string join)
            {
                StartTest(JoinWhenReady(join));
            }

            // "--dwmp-dummy": leave a test dummy once in a world.
            if (Has("--dwmp-dummy"))
            {
                StartTest(DummyWhenInWorld());
            }

            // "--dwmp-test-menu-entry": check that the menu's list takes a
            // click, Join Game in the main menu (or, with --dwmp-continue,
            // Multiplayer in the pause menu), and opens the co-op panel.
            if (Has("--dwmp-test-menu-entry"))
            {
                StartTest(
                    MenuEntryTest(Has("--dwmp-continue"), Arg("--dwmp-shots")));
            }

            // "--dwmp-screen WxH": a window of that size once the main menu is
            // up (for checking other screen shapes; with --dwmp-shots DIR,
            // screenshots of the menu and, with --dwmp-continue, the world and
            // pause menu).
            // "--dwmp-test-start-window": open the Start Game window from the
            // main menu (see StartGameWindow.Test for --dwmp-sandbox,
            // --dwmp-screen, --dwmp-shots, --dwmp-start, --dwmp-start-host,
            // --dwmp-start-name).
            StartTest window = TestArgRules.WindowTest(args);
            if (window == Testing.StartTest.StartWindow)
            {
                StartTest(
                    StartGameWindow.Test(
                        Arg("--dwmp-sandbox"), Arg("--dwmp-screen"),
                        Arg("--dwmp-shots"), Arg("--dwmp-start"),
                        Arg("--dwmp-start-host"), Arg("--dwmp-start-name")));
            }
            else if (window == Testing.StartTest.Screen)
            {
                string screen = Arg("--dwmp-screen");
                StartTest(
                    ScreenTest(
                        screen, Arg("--dwmp-shots"), Has("--dwmp-continue")));
            }

            // "--dwmp-testworld": start the test world from the main menu
            // ("--dwmp-reset": from a fresh copy); with --dwmp-shots DIR,
            // screenshots of the area once it's built.
            if (Has("--dwmp-testworld"))
            {
                StartTest(
                    _host.TestArea.Start(
                        _host.Share, reset: Has("--dwmp-reset")));
                if (Arg("--dwmp-shots") is string tdir)
                {
                    StartTest(TestWorldShots(tdir));
                }
            }

            // "--dwmp-dump-prefabs": once in a world, log every prefab the save
            // system knows (name -> path).
            if (Has("--dwmp-dump-prefabs"))
            {
                StartTest(DumpPrefabs());
            }

            // "--dwmp-dump-ui": log the main menu's objects (and the fonts
            // loaded), for building UI that matches it.
            if (Has("--dwmp-dump-ui"))
            {
                StartTest(DumpUi());
            }

            // "--dwmp-shots DIR": screenshots of the menus, the Multiplayer
            // window, the server browser and its connect dialog (and, in a
            // world with a player in, the pause menu) into DIR.
            // "--dwmp-solo-test DIR" (with --dwmp-continue N --dwmp-sandbox, no
            // --dwmp-host): playing alone, the pause menu over the world and a
            // skill reset at the stove, with screenshots into DIR.
            // "--dwmp-test-options DIR": the Options window in the main menu
            // (or, with --dwmp-continue, the pause menu): screenshots of every
            // tab at 1920x1080 and 1280x720 into DIR, and a check that a volume
            // and a key changed there reach the game's stored settings, which
            // are then put back as they were ("[options]" lines).
            if (Arg("--dwmp-test-options") is string optDir)
            {
                StartTest(
                    _host.OptionsTest.Run(optDir, Has("--dwmp-continue")));
            }

            // "--dwmp-test-drag": a window moved and resized by made-up mouse
            // events, slowly and fast ("[drag]" lines).
            if (Has("--dwmp-test-drag"))
            {
                StartTest(_host.Drag.Run());
            }

            // "--dwmp-test-tooltips DIR": once in a world, dump the items' and
            // map elements' data into DIR and check that each has a description
            // (the game's or the mod's, World/Tooltips.cs) ("[tooltips]"
            // lines).
            if (Arg("--dwmp-test-tooltips") is string tipDir)
            {
                StartTest(_host.Tooltips.Test(tipDir));
            }

            // "--dwmp-test-drag-real": the test window open for two minutes for
            // a real mouse to drag ("[drag-real]" lines).
            if (Has("--dwmp-test-drag-real"))
            {
                StartTest(_host.Drag.RunReal());
            }

            if (Arg("--dwmp-solo-test") is string solo)
            {
                StartTest(SoloTest(solo));
            }

            if (TestArgRules.ShotsAlone(args))
            {
                StartTest(Shots(Arg("--dwmp-shots"), Has("--dwmp-continue")));
            }
        }

        /// <summary>
        /// Between tests: stop what the last one started and leave its session
        /// (TestControl's reset).
        /// </summary>
        internal void StopTests()
        {
            foreach (var c in _tests)
            {
                if (c != null)
                {
                    StopCoroutine(c);
                }
            }

            _tests.Clear();
            ITestSteps steps = TestSteps;
            if (steps != null)
            {
                steps.Stop();
            }

            _host.OptionsTest.RestoreIfCutShort();
            // The switches the map steps and the solo tests turn on, back to
            // how a player's game has them.
            CoopSession session = Coop.Current;
            if (session != null)
            {
                session.Ui.Enabled = true;
                session.Pushes.TestTouch = null;
                session.Motion.Stats.On = false;
                session.Traps.TestRefuse = World.Traps.TrapTest.None;
            }

            _host.Share.TestHoldSecs = 0f;

            _host.SkillsPanel.TestMouse = null;
            _host.TestMouse = null;
            _host.TestAsPlayer = false;
            var player = Player.Instance;
            if (player != null)
            {
                player.inShop = false;
                player.talkedToNPC = null;
            }

            _host.Threats.Debug = false;
            _host.DebugWorld = false;
            _host.LoadSpeed.Verify = false;
            _host.TestQuickEnd = false;
            _host.SpawnType = null;
            _host.Presence.Logged = false;
            TcpBackend.TestMaxPlayers = 0;
            _host.Confirm.Cancel();
            if (_host.Session.Active)
            {
                _host.Session.Backend.Leave();
            }
        }
    }
}
