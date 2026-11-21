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
        /// <summary>
        /// Logs each time the main menu, and then a world, is ready, and how
        /// long it took. The tests wait for these lines ("Main menu ready",
        /// "World ready").
        /// </summary>
        private System.Collections.IEnumerator LogStartTimes()
        {
            bool first = true;
            while (true)
            {
                while (!WorldShare.MainMenuReady())
                {
                    yield return null;
                }

                Log.LogInfo(
                    $"Main menu ready {Time.realtimeSinceStartup:0.0} s after "
                        + "start");
                while (WorldShare.MainMenuReady())
                {
                    yield return null;
                }

                float left = Time.realtimeSinceStartup;
                while (!WorldShare.WorldReady && !WorldShare.MainMenuReady())
                {
                    yield return null;
                }

                if (!WorldShare.WorldReady)
                {
                    continue;
                }

                Log.LogInfo(
                    $"World ready {Time.realtimeSinceStartup:0.0} s after "
                        + $"start, {Time.realtimeSinceStartup - left:0.0} s "
                        + "after leaving the menu");
                if (first)
                {
                    _host.LoadProfiler.Report();
                }

                if (first && _host.LoadSpeed.Verify)
                {
                    Log.LogInfo(
                        $"LoadSpeed: checked {_host.LoadSpeed.Checked} objects'"
                            + $" grid nodes, {_host.LoadSpeed.Mismatches}"
                            + " different from the game's search");
                }

                first = false;
                while (WorldShare.InWorld)
                {
                    yield return null;
                }
            }
        }

        /// <summary>
        /// A screenshot into <paramref name="path"/>: waits
        /// <paramref name="before"/> seconds, takes it, waits
        /// <paramref name="after"/>, then logs <paramref name="log"/> if given.
        /// No path: nothing.
        /// </summary>
        private static System.Collections.IEnumerator Screenshot(
            string path, float before = 0f, float after = 0.5f,
            string log = null)
        {
            if (path == null)
            {
                yield break;
            }

            if (before > 0f)
            {
                yield return new WaitForSecondsRealtime(before);
            }

            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(after);
            if (log != null)
            {
                Log.LogInfo(log);
            }
        }

        private System.Collections.IEnumerator Shots(
            string dir, bool inWorldToo)
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Collections.IEnumerator Shot(string name)
            {
                string path = System.IO.Path.Combine(dir, name + ".png");
                return Screenshot(path, before: 1.5f, log: $"[shots] {path}");
            }

            if (!inWorldToo)
            {
                while (!WorldShare.MainMenuReady())
                {
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(4f);
                yield return Shot("main-menu");
                Log.LogInfo($"[shots] {_host.MenuList.Check()}");
                _host.MenuList.Click("Quit");
                yield return Shot("main-menu-quit");
                _host.Confirm.Cancel();
                _host.MenuList.Click("Join Game");
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

            if (!inWorldToo)
            {
                Log.LogInfo("[shots] done");
                yield break;
            }

            while (!WorldShare.WorldReady)
            {
                yield return null;
            }

            while (_host.Session.Active && _host.Session.Peers.Count == 0)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(8f);
            yield return Shot("world");
            string hudBefore = _host.PauseHud.Shown();
            Log.LogInfo($"[shots] world: HUD shown {hudBefore}");
            ShotsUi("world", hidden: false);
            // Core.HideUI left true by an earlier world (the epilogue, the
            // credits) with the HUD back on: tags still drawn.
            Core.HideUI = true;
            ShotsUi("world-stale-hide", hidden: false);
            Core.HideUI = false;
            Core.showHidePauseMenu();
            float clockBefore = Singleton<Controller>.Instance.CurrentTime;
            yield return Shot("pause-menu");
            Log.LogInfo($"[shots] {_host.MenuList.Check()}");
            ShotsUi("pause-menu", hidden: true);
            ShotsHud("pause-menu", "none");
            _host.MenuList.Click("Back to main menu");
            yield return Shot("pause-menu-confirm");
            _host.Confirm.Cancel();
            yield return new WaitForSecondsRealtime(1.5f);
            Log.LogInfo(
                $"[shots] pause menu open: time scale {Time.timeScale}, paused "
                    + $"{Core.Paused}, clock {clockBefore} -> "
                    + $"{Singleton<Controller>.Instance.CurrentTime}");
            OpenPanel();
            yield return Shot("pause-menu-panel");
            SetPanel(false);
            Core.showHidePauseMenu();
            yield return Shot("world-after");
            ShotsUi("world-after", hidden: false);
            ShotsHud("world-after", hudBefore);
            Log.LogInfo("[shots] done");
        }

        /// <summary>
        /// The shots test: the HUD shown at this shot (PauseHud.Shown), as it
        /// should be.
        /// </summary>
        private static void ShotsHud(string shot, string want)
        {
            string now = Instance._host.PauseHud.Shown();
            string text = "FAIL";
            if (now == want)
            {
                text = "PASS";
            }

            Log.LogInfo($"[shots] {shot}: {text} HUD shown {now}, want {want}");
        }

        /// <summary>
        /// The shots test: whether world overlays (name tags) are hidden at
        /// this shot, as they should be.
        /// </summary>
        private static void ShotsUi(string shot, bool hidden)
        {
            bool now = Menus.UiState.WorldOverlaysHidden;
            string text = "FAIL";
            if (now == hidden)
            {
                text = "PASS";
            }

            string reason = Menus.UiState.Reason;
            if (reason == null)
            {
                reason = "the world in play";
            }

            Log.LogInfo(
                $"[shots] {shot}: {text} world overlays hidden {now} "
                    + $"({reason}), want {hidden}");
        }

        private System.Collections.IEnumerator ScreenTest(
            string size, string shots, bool inWorld)
        {
            var wh = size.Split('x');
            int w = int.Parse(wh[0]), h = int.Parse(wh[1]);
            System.Collections.IEnumerator Shot(string name)
            {
                string path = null;
                if (shots != null)
                {
                    path = System.IO.Path.Combine(shots, $"{size}-{name}.png");
                }

                return Screenshot(path);
            }

            // Loading a world sets the game's own resolution again, so in a
            // world the size is set once it's up.
            if (!inWorld)
            {
                while (!WorldShare.MainMenuReady())
                {
                    yield return null;
                }

                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(3f);
                Log.LogInfo(
                    $"[screen] asked {w}x{h}, got "
                        + $"{Screen.width}x{Screen.height}; mouse "
                        + $"{UnityEngine.Cursor.lockState}");
                yield return Shot("menu");
                _host.MenuList.Click("Quit");
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("quit");
                _host.Confirm.Cancel();
                yield return new WaitForSecondsRealtime(0.5f);
                _host.MenuList.Click("Join Game");
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("panel");
                _direct.ShowForShots(address: false);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("browser");
                SetPanel(false);
                Log.LogInfo("[screen] done");
                yield break;
            }

            while (!WorldShare.WorldReady)
            {
                yield return null;
            }

            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(4f);
            Log.LogInfo(
                $"[screen] in the world: {Screen.width}x{Screen.height}; zoom "
                    + $"{Singleton<Controller>.Instance.cameraZoom}; mouse "
                    + $"{UnityEngine.Cursor.lockState}");
            yield return Shot("world");
            Player.Instance.openInventory();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("inventory");
            Player.Instance.closeInventory();
            yield return new WaitForSecondsRealtime(1f);
            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("pause");
            Log.LogInfo(
                $"[screen] pause menu: mouse {UnityEngine.Cursor.lockState}");
            _host.MenuList.Click("Quit");
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("pause-quit");
            _host.Confirm.Cancel();
            yield return new WaitForSecondsRealtime(0.5f);
            Core.showHidePauseMenu();
            Log.LogInfo("[screen] done");
        }

        private System.Collections.IEnumerator MenuEntryTest(
            bool pauseMenuToo, string shots)
        {
            if (!pauseMenuToo)
            {
                while (!WorldShare.MainMenuReady())
                {
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(2f);
                yield return CheckMenuEntry("main menu", shots);
                yield break;
            }

            // Loading the world turns the menu's buttons off at once, so the
            // main menu is checked on its own.
            while (!WorldShare.WorldReady)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(3f);
            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(2f);
            yield return CheckMenuEntry("pause menu", shots);
            Core.showHidePauseMenu();
        }

        /// <summary>
        /// With a screenshot of the panel it opened into DIR, if given.
        /// </summary>
        private System.Collections.IEnumerator CheckMenuEntry(
            string where, string shots)
        {
            Log.LogInfo($"[menu-entry] {where}: {_host.MenuList.Check()}");
            string label = "Multiplayer";
            if (where == "main menu")
            {
                label = "Join Game";
            }

            bool took = _host.MenuList.Click(label);
            yield return null;
            yield return null;
            Log.LogInfo(
                $"[menu-entry] {where}: clicked, panel open: {_showMenu} "
                    + $"(entry taken: {took})");
            if (shots != null)
            {
                yield return Screenshot(
                    System.IO.Path.Combine(
                        shots, $"panel-{where.Replace(' ', '-')}.png"),
                    before: 1f);
            }

            SetPanel(false);
            yield return null;
        }

        private System.Collections.IEnumerator TestWorldShots(string dir)
        {
            while (!TestWorld.Active || !WorldShare.WorldReady)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(6f);
            // Out of the house and turned towards the area's west side, so some
            // of it is in sight.
            var p = Player.Instance.transform;
            p.position += new Vector3(-300f, 0f, 250f);
            yield return new WaitForSecondsRealtime(2f);
            yield return Screenshot(
                System.IO.Path.Combine(dir, "testworld-area.png"), after: 1f);
            OpenPanel();
            yield return Screenshot(
                System.IO.Path.Combine(dir, "testworld-panel.png"), before: 1f,
                after: 1f);
            SetPanel(false);
            // The nearest workbench open, for its storage and the Sort & Stack
            // button under it.
            var wb = System.Linq.Enumerable.FirstOrDefault(
                System.Linq.Enumerable.OrderBy(
                    UnityEngine.Object.FindObjectsOfType<Workbench>(),
                    w => (w.transform.position - p.position).sqrMagnitude));
            if (wb != null && wb.normalInventory != null)
            {
                yield return new WaitForSecondsRealtime(1f);
                wb.open();
                yield return Screenshot(
                    System.IO.Path.Combine(dir, "testworld-workbench.png"),
                    before: 1.5f, after: 1f);
                Player.Instance.closeInventory();
            }

            Log.LogInfo("[testworld] shots done");
        }
    }
}
