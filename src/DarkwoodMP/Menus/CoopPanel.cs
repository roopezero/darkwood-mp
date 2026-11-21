using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The Multiplayer window (the co-op panel) with the Direct panel and its
    /// server browser. Process state, on ModHost: it opens from the main menu.
    /// </summary>
    internal sealed class CoopPanel
    {
        private readonly ModHost _host;
        // starts the test world's coroutines
        private readonly MonoBehaviour _runner;
        private readonly UiWindow _panel;
        private readonly DirectPanel _direct;
        // the system cursor's visibility before the panel showed it
        private bool? _cursorBefore;
        private bool _showMenu;
        private bool _buttonsOffByUs;

        public CoopPanel(ModHost host, MonoBehaviour runner)
        {
            _host = host;
            _runner = runner;
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
        }

        /// <summary>The panel is open.</summary>
        public bool IsOpen => _showMenu;

        /// <summary>The Direct panel (the screenshot tests show it).</summary>
        public DirectPanel Direct => _direct;

        /// <summary>
        /// Each frame, first: Escape and a closed server browser close the
        /// panel.
        /// </summary>
        public void Update()
        {
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
        }

        /// <summary>
        /// Each frame, after the test control: the game's buttons are off
        /// while our windows are over its menus.
        /// </summary>
        public void HoldGameButtons()
        {
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
        }

        public void SetPanel(bool show)
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
        /// The main menu's Join Game: the server browser alone.
        /// </summary>
        public void OpenJoin()
        {
            _showMenu = true;
            _panel.Open = false;
            _direct.OpenServerBrowser();
        }

        /// <summary>
        /// The panel and the menu windows over the game, after the name
        /// tags.
        /// </summary>
        public void OnGUI()
        {
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
            else if (Plugin.Instance.Host.SaveFolders.Active)
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

            if (!_host.Session.Active && Plugin.CantHostReason is string why)
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
                if (_host.SteamStart.Missing)
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

            if (Plugin.TestMode)
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
                    _runner.StartCoroutine(
                        _host.TestArea.Start(_host.Share, reset: false));
                }

                if (TestWorld.Exists && Ui.Button(r[1], "Reset it"))
                {
                    SetPanel(false);
                    _runner.StartCoroutine(
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
    }
}
