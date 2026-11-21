using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DarkwoodMP.Menus;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// Direct connections in the co-op windows: the Multiplayer window's Direct
    /// section (hosting, or the buttons that open the browser), and the windows
    /// it opens, after Half-Life's: the server browser (Favorites, History,
    /// LAN, with sortable columns), "Connect to address" and the password
    /// prompt.
    /// </summary>
    public sealed class DirectPanel
    {
        private readonly TcpBackend _tcp;
        private readonly ServerBrowser _browser;
        private readonly Session _session;

        private readonly UiWindow _browserWin, _addressWin, _passwordWin;

        private ServerBrowser.Tab _tab = ServerBrowser.Tab.Favorites;
        private string _selected;
        private Vector2 _scroll;
        private bool _refreshedOnce;
        private float _lastClick;
        private string _lastClicked;
        private readonly BrowserOrder _order = new BrowserOrder();

        private string _address = "", _addressPassword = "";
        private string _passwordFor, _password = "";
        private bool _focusField;

        private string _publicIp;
        private bool _lookingUp;

        private static readonly string[] Tabs =
            { "Favorites", "History", "LAN" };
        // The browser's columns, as Half-Life's: (locked), name, players, the
        // world's day, ping. Widths at scale 1; 0 shares the rest.
        private static readonly string[] Columns =
            { "", "Server", "Players", "World", "Ping" };
        private static readonly float[] ColumnWidths = { 18, 0, 62, 110, 50 };

        public DirectPanel(
            TcpBackend tcp, ServerBrowser browser, Session session)
        {
            _tcp = tcp;
            _browser = browser;
            _session = session;
            float S() => PixelFont.Scale;
            _browserWin = new UiWindow(0x0D3B, "Servers")
            {
                MinSize = new Vector2(380, 200),
                DefaultRect = () => UiWindow.RightSide(560 * S(), 330 * S()),
                Body = DrawBrowser,
                Footer = BrowserButtons,
                FooterHeight = 13 + 8,
            };

            _addressWin = new UiWindow(0x0D3C, "Connect to address")
            {
                MinSize = new Vector2(280, 150),
                FitContent = true,
                DefaultRect = () => UiWindow.RightSide(330 * S(), 170 * S()),
                Body = DrawAddress,
                Footer = AddressButtons,
                FooterHeight = 13 + 8,
            };

            _passwordWin = new UiWindow(0x0D3D, "Password")
            {
                MinSize = new Vector2(260, 120),
                FitContent = true,
                DefaultRect = () => UiWindow.RightSide(300 * S(), 130 * S()),
                Body = DrawPassword,
                Footer = PasswordButtons,
                FooterHeight = 13 + 8,
            };
        }

        /// <summary>
        /// The windows this section opened (drawn after the Multiplayer window,
        /// so they're on top).
        /// </summary>
        public void OnGUI()
        {
            _browserWin.OnGUI();
            _addressWin.OnGUI();
            _passwordWin.OnGUI();
        }

        private string _shotRow;

        public void CloseAll()
        {
            if (_shotRow != null)
            {
                _browser.Favorites.Remove(_shotRow);
                _shotRow = null;
            }

            CloseWindows();
        }

        private void CloseWindows()
        {
            _browserWin.Close();
            _addressWin.Close();
            _passwordWin.Close();
        }

        /// <summary>
        /// For the screenshots: the browser, with the address dialog over it if
        /// asked.
        /// </summary>
        internal void ShowForShots(bool address)
        {
            // A row to show, kept out of the saved list (nothing answers on
            // it).
            if (_browser.Favorites.Count == 0)
            {
                _shotRow = "127.0.0.1:27799";
                _browser.Favorites.Add(_shotRow);
                _browser.Query(_shotRow);
            }

            OpenServerBrowser();
            if (address)
            {
                OpenAddress();
            }
        }

        // ---- the Multiplayer window's Direct section ------------------------

        public void Draw(Flow f)
        {
            DirectView view = DirectRules.View(FactsOf(_session));
            if (view == DirectView.SteamHost)
            {
                Ui.Text(f, "In a Steam game.", MenuSkin.Dim);
                return;
            }

            if (view == DirectView.SteamClient)
            {
                Ui.Text(
                    f,
                    "In a Steam game. Joining a server here leaves it.",
                    MenuSkin.Dim);
                JoinButtons(f);
                return;
            }

            if (view == DirectView.DirectGame)
            {
                DrawInSession(f);
                return;
            }

            string refusal = _session.LastRefusal;
            string line = DirectRules.IdleLine(refusal, _tcp.Status);
            if (refusal != null)
            {
                Ui.Text(f, line, MenuSkin.Warn);
            }
            else if (line != null)
            {
                Ui.Text(f, line);
            }

            JoinButtons(f);

            f.Space(4);
            if (Plugin.CantHostReason != null)
            {
                return; // said once, at the top of the window
            }

            var cfg = Plugin.Cfg;
            cfg.ServerName.Value =
                Ui.Field(f, "Server name", cfg.ServerName.Value, 48);
            string port =
                Ui.Field(f, "Port", cfg.DirectPort.Value.ToString(), 5);
            cfg.DirectPort.Value =
                HostingRules.TypedPort(port, cfg.DirectPort.Value);

            cfg.Password.Value =
                Ui.Field(f, "Password", cfg.Password.Value, 32, password: true);
            var m = f.Row(Ui.RowH, 90, 24, 30, 24, 0);
            Ui.Label(m[0], "Max players");
            bool fewer = HostingRules.MayRemovePlayer(cfg.MaxPlayers.Value);
            if (Ui.Button(m[1], "-", fewer))
            {
                cfg.MaxPlayers.Value--;
            }

            Ui.Label(
                m[2], cfg.MaxPlayers.Value.ToString(), MenuSkin.Bright,
                TextAnchor.MiddleCenter);
            bool more = HostingRules.MayAddPlayer(cfg.MaxPlayers.Value);
            if (Ui.Button(m[3], "+", more))
            {
                cfg.MaxPlayers.Value++;
            }

            cfg.OpenPort.Value = Ui.Check(
                f.Next(Ui.RowH), cfg.OpenPort.Value,
                "Open the port on my router (UPnP)");
            Ui.Text(
                f, PortHint(cfg.DirectPort.Value, cfg.OpenPort.Value),
                MenuSkin.Dim);
            if (Ui.Button(f, "Start hosting"))
            {
                _session.LastRefusal = null;
                _tcp.Host(
                    cfg.ServerName.Value, cfg.DirectPort.Value,
                    cfg.Password.Value, cfg.MaxPlayers.Value);
            }
        }

        private void DrawInSession(Flow f)
        {
            Ui.Text(f, _tcp.Status, MenuSkin.Bright);
            if (_tcp.IsHost)
            {
                Ui.Text(
                    f,
                    "Friends find you in their LAN tab, or add one of these:",
                    MenuSkin.Dim);
                foreach (var ip in TcpBackend.LocalAddresses(new BepInLog()))
                {
                    AddressRow(f, $"{ip}:{_tcp.Port}", "LAN / VPN");
                }

                var map = _tcp.Mapper;
                InternetRow row = DirectRules.Internet(FactsOf(map));
                if (row.Address != null)
                {
                    AddressRow(f, row.Address, row.Note);
                }
                else
                {
                    string label = "Show my public IP (asks api.ipify.org)";
                    if (_lookingUp)
                    {
                        label = "Looking up...";
                    }

                    if (Ui.Button(f, label) && !_lookingUp)
                    {
                        LookUpPublicIp();
                    }
                }

                if (map.Status.Length > 0)
                {
                    string status = map.Status;
                    Color? color;
                    if (map.Mapped && !map.BehindAnotherNat)
                    {
                        color = null;
                    }
                    else
                    {
                        color = MenuSkin.Warn;
                    }

                    Ui.Text(f, status, color);
                }
            }

            if (DirectRules.MayReload(_tcp.IsHost, _session.Connected)
                && Ui.Button(
                    f, "Reload the host's world (if things look out of sync)"))
            {
                _tcp.Rejoin(); // sends your character first
            }

            string text = "Leave";
            if (_tcp.IsHost)
            {
                text = "Stop hosting";
            }

            if (Ui.Button(f, text))
            {
                _tcp.Leave();
            }

            // A joiner can go straight to another server (or this one again):
            // Connect leaves this game first.
            if (!_tcp.IsHost)
            {
                JoinButtons(f);
            }
        }

        private void JoinButtons(Flow f)
        {
            var row = f.Row(Ui.RowH, 0, 0);
            if (Ui.Button(row[0], "Find servers..."))
            {
                OpenServerBrowser();
            }

            if (Ui.Button(row[1], "Connect to address..."))
            {
                OpenAddress();
            }
        }

        private static void AddressRow(Flow f, string addr, string note)
        {
            var c = f.Row(Ui.RowH, 0, 60);
            Ui.Label(c[0], PixelFont.Fit($"{addr}  ({note})", c[0].width));
            if (Ui.Button(c[1], "Copy"))
            {
                GUIUtility.systemCopyBuffer = addr;
            }
        }

        /// <summary>
        /// What the host's friends connect on, and what the host has to do for
        /// internet play.
        /// </summary>
        public static string PortHint(int port, bool openPort)
        {
            if (openPort)
            {
                return $"Friends connect on TCP port {port}, and the browser "
                    + $"asks on UDP {port}. Your router is asked to forward "
                    + "both; if it can't, the window says so: forward them by "
                    + "hand, or use a VPN (Tailscale, ZeroTier).";
            }

            return $"Friends connect on TCP port {port}, and the browser asks "
                + $"on UDP {port}. Over the internet, forward both, or use a "
                + "VPN (Tailscale, ZeroTier).";
        }

        private void LookUpPublicIp()
        {
            _lookingUp = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var wc = new WebClient())
                    {
                        string ip =
                            wc.DownloadString("https://api.ipify.org").Trim();
                        if (IPAddress.TryParse(ip, out IPAddress _))
                        {
                            _publicIp = ip;
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Public IP lookup failed: {e.Message}");
                }
                finally
                {
                    _lookingUp = false;
                }
            });
        }

        // ---- the server browser ---------------------------------------------

        /// <summary>
        /// Opens the server browser (also the main menu's Join Game); the first
        /// time, it asks the Favorites and History servers.
        /// </summary>
        public void OpenServerBrowser()
        {
            if (!_refreshedOnce)
            {
                _refreshedOnce = true;
                _browser.Refresh(ServerBrowser.Tab.Favorites);
                _browser.Refresh(ServerBrowser.Tab.History);
            }

            _browserWin.Show();
        }

        /// <summary>
        /// One of this section's windows (the browser, the address or the
        /// password dialog) is open.
        /// </summary>
        public bool AnyOpen =>
            _browserWin.Open || _addressWin.Open || _passwordWin.Open;

        /// <summary>
        /// Called as a join starts (the windows close then), so the Multiplayer
        /// window can show how it goes.
        /// </summary>
        public Action OnJoining;

        private void SwitchTab(ServerBrowser.Tab t)
        {
            if (t == _tab)
            {
                return;
            }

            _tab = t;
            _selected = null;
            _scroll = Vector2.zero;
            _browser.Refresh(_tab);
        }

        private void DrawBrowser(Flow f)
        {
            float s = PixelFont.Scale;
            var tabs = Ui.Split(
                new Rect(f.Area.x, f.Y, 80 * 3 * s + 8 * s, Ui.RowH), 80, 80,
                80);
            for (int i = 0; i < Tabs.Length; i++)
            {
                if (Ui.Tab(tabs[i], Tabs[i], (int)_tab == i))
                {
                    SwitchTab((ServerBrowser.Tab)i);
                }
            }

            f.Y += Ui.RowH;

            // The list fills the window above the status line.
            float statusH = Ui.RowH;
            var panel = new Rect(
                f.Area.x, f.Y, f.Width,
                Mathf.Max(
                    60 * s,
                    f.Area.height - (f.Y - f.Area.y) - statusH - f.Gap));
            GUI.Box(panel, GUIContent.none, MenuSkin.Sunken);
            var inner = new Rect(
                panel.x + 2 * s, panel.y + 2 * s, panel.width - 4 * s,
                panel.height - 4 * s);

            // Column headings: click to sort, again to turn the order round.
            float headH = PixelFont.Line + 6 * s;
            float scrollW = 10 * s;
            var cols = Ui.Split(
                new Rect(inner.x, inner.y, inner.width - scrollW, headH),
                ColumnWidths);
            for (int c = 0; c < Columns.Length; c++)
            {
                var r = new Rect(
                    cols[c].x, cols[c].y, cols[c].width + 4 * s,
                    cols[c].height);
                if (GUI.Button(r, GUIContent.none, MenuSkin.Button))
                {
                    _order.Click(c);
                }

                string arrow = "";
                if (_order.Column == c && c > 0)
                {
                    if (_order.Down)
                    {
                        arrow = "  v";
                    }
                    else
                    {
                        arrow = "  ^";
                    }
                }

                string name = Columns[c] + arrow;
                Color color;
                if (r.Contains(Event.current.mousePosition))
                {
                    color = MenuSkin.Bright;
                }
                else
                {
                    color = MenuSkin.Text;
                }

                Ui.Label(
                    new Rect(r.x + 4 * s, r.y, r.width - 6 * s, r.height), name,
                    color);
            }

            var rows = Sorted(_browser.List(_tab));
            float rowH = PixelFont.Line + 5 * s;
            var listArea = new Rect(
                inner.x, inner.y + headH, inner.width, inner.height - headH);
            var view = new Rect(
                0, 0, listArea.width - scrollW,
                Mathf.Max(listArea.height, rows.Count * rowH));
            _scroll = GUI.BeginScrollView(
                listArea, _scroll, view, false,
                rows.Count * rowH > listArea.height, GUIStyle.none,
                GUI.skin.verticalScrollbar);
            if (rows.Count == 0)
            {
                string empty = "You haven't joined any servers yet.";
                if (_tab == ServerBrowser.Tab.Lan)
                {
                    if (_browser.SearchingLan)
                    {
                        empty = "Searching the LAN...";
                    }
                    else
                    {
                        empty = "No servers found on the LAN.";
                    }
                }
                else if (_tab == ServerBrowser.Tab.Favorites)
                {
                    empty = "No favorites yet: Add server puts one here.";
                }

                Ui.Label(
                    new Rect(6 * s, 4 * s, view.width - 12 * s, rowH), empty,
                    MenuSkin.Dim);
            }

            var rc = Ui.Split(new Rect(0, 0, view.width, rowH), ColumnWidths);
            for (int i = 0; i < rows.Count; i++)
            {
                string addr = rows[i];
                var e = _browser.Get(addr);
                var row = new Rect(0, i * rowH, view.width, rowH);
                bool sel = _selected == addr;
                if (sel)
                {
                    GUI.Box(row, GUIContent.none, MenuSkin.Selected);
                }

                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0
                    && row.Contains(ev.mousePosition))
                {
                    bool twice = JoinRules.IsDoubleClick(ClickOf(addr));
                    _selected = addr;
                    _lastClicked = addr;
                    _lastClick = Time.unscaledTime;
                    ev.Use();
                    if (twice)
                    {
                        Connect(addr);
                    }
                }

                var info = e.Info;
                bool ok = CanJoin(addr);
                Color col;
                if (!ok || (info == null && !e.Querying))
                {
                    col = MenuSkin.Dim;
                }
                else if (sel)
                {
                    col = MenuSkin.Bright;
                }
                else
                {
                    col = MenuSkin.Text;
                }

                float y = row.y;
                Rect C(int c) =>
                    new Rect(rc[c].x + 4 * s, y, rc[c].width - 4 * s, rowH);
                if (info != null && info.Password)
                {
                    DrawLock(C(0), col);
                }

                string name;
                if (info != null)
                {
                    name = info.Name;
                }
                else
                {
                    name = addr;
                }

                Ui.Label(C(1), PixelFont.Fit(name, C(1).width), col);
                string players = "";
                if (info != null)
                {
                    players = $"{info.Players}/{info.MaxPlayers}";
                }

                Ui.Label(C(2), players, col);
                string world = "";
                if (info == null)
                {
                    if (e.Querying)
                    {
                        world = "...";
                    }
                    else
                    {
                        world = "no answer";
                    }
                }
                else if (info.Incompatibility != null)
                {
                    world = "other version";
                }
                else if (info.Fields.TryGetValue("world", out var wl))
                {
                    world = wl;
                }

                Ui.Label(C(3), PixelFont.Fit(world, C(3).width), col);
                string ping = "";
                if (info != null)
                {
                    ping = e.PingMs.ToString();
                }

                Ui.Label(C(4), ping, col);
            }

            GUI.EndScrollView();
            f.Y = panel.yMax + f.Gap;

            // The status line: the chosen server's address and what's wrong
            // with it, or how many there are.
            string status;
            if (_selected != null)
            {
                var info = _browser.Get(_selected).Info;
                string detail = "";
                if (info != null)
                {
                    string why = info.Incompatibility;
                    if (why != null)
                    {
                        detail = $"  -  {why}";
                    }
                    else
                    {
                        detail = $"  -  DarkwoodMP {info.Version}";
                    }
                }

                status = _selected + detail;
            }
            else
            {
                if (_tab == ServerBrowser.Tab.Lan && _browser.SearchingLan)
                {
                    status = "Searching the LAN...";
                }
                else
                {
                    string text = "s";
                    if (rows.Count == 1)
                    {
                        text = "";
                    }

                    status = $"{rows.Count} server{text}";
                }
            }

            // Beside it, while no name is set (the Steam or OS user name is
            // used), where to set one.
            string hint = NameRules.BrowserHint(
                _session.Active, Plugin.Cfg.PlayerName.Value,
                _session.LocalName);

            float hintW = 0f;
            if (hint != null)
            {
                hintW =
                    Mathf.Min(PixelFont.Width(hint) + 8 * s, f.Width * 0.6f);
            }

            float gap = 0f;
            if (hint != null)
            {
                gap = 8 * s;
            }

            float statusW = f.Width - hintW - gap;
            Ui.Label(
                new Rect(f.Area.x + 2 * s, f.Y, statusW, statusH),
                PixelFont.Fit(status, statusW), MenuSkin.Dim);
            if (hint != null)
            {
                Ui.Label(
                    new Rect(f.Area.xMax - hintW, f.Y, hintW, statusH),
                    PixelFont.Fit(hint, hintW), MenuSkin.Dim,
                    TextAnchor.MiddleRight);
            }

            f.Y += statusH;
        }

        /// <summary>
        /// A small padlock: shackle and body, in the row's colour.
        /// </summary>
        private static void DrawLock(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float s = PixelFont.Scale;
            float x = Mathf.Round(r.x + 1 * s),
                y = Mathf.Round(r.y + (r.height - 9 * s) / 2f);
            var old = GUI.color;
            GUI.color = c * old;
            var t = MenuSkin.White;
            GUI.DrawTexture(new Rect(x + 1 * s, y, 5 * s, s), t);
            GUI.DrawTexture(new Rect(x + 1 * s, y, s, 4 * s), t);
            GUI.DrawTexture(new Rect(x + 5 * s, y, s, 4 * s), t);
            GUI.DrawTexture(new Rect(x, y + 4 * s, 7 * s, 5 * s), t);
            GUI.color = old;
        }

        private List<string> Sorted(List<string> list)
        {
            var rows = list.Select(RowOf).ToList();
            return _order.Sorted(rows, _tab == ServerBrowser.Tab.Lan);
        }

        /// <summary>The browser's entry for a server, for its sort.</summary>
        private ServerRow RowOf(string address)
        {
            var entry = _browser.Get(address);
            var info = entry.Info;
            if (info == null)
            {
                return new ServerRow { Address = address };
            }

            info.Fields.TryGetValue("world", out var world);
            return new ServerRow
            {
                Address = address,
                Known = true,
                Password = info.Password,
                Name = info.Name,
                Players = info.Players,
                World = world,
                Ping = entry.PingMs,
            };
        }

        private void BrowserButtons(Rect r)
        {
            var b = Ui.Split(r, 100, 80, 120, 0, 80, 80);
            int slot = 0;
            if (Ui.Button(b[slot++], "Add server..."))
            {
                OpenAddress();
            }

            if (_tab != ServerBrowser.Tab.Lan
                && Ui.Button(b[slot++], "Remove", _selected != null))
            {
                if (_tab == ServerBrowser.Tab.Favorites)
                {
                    _browser.RemoveFavorite(_selected);
                }
                else
                {
                    _browser.RemoveHistory(_selected);
                }

                _selected = null;
            }

            if (_tab != ServerBrowser.Tab.Favorites)
            {
                var fav =
                    new Rect(b[slot].x, r.y, 120 * PixelFont.Scale, r.height);
                if (Ui.Button(
                    fav, "Add to favorites",
                    _selected != null
                        && !_browser.Favorites.Contains(_selected)))
                {
                    _browser.AddFavorite(_selected);
                }
            }

            string text = "Refresh";
            if (_tab == ServerBrowser.Tab.Lan && _browser.SearchingLan)
            {
                text = "Searching";
            }

            if (Ui.Button(b[4], text))
            {
                _browser.Refresh(_tab);
            }

            if (Ui.Button(
                b[5], "Connect", _selected != null && CanJoin(_selected)))
            {
                Connect(_selected);
            }
        }

        // ---- connecting -----------------------------------------------------

        private bool CanJoin(string addr)
        {
            var info = _browser.Get(addr).Info;
            bool incompatible = info != null && info.Incompatibility != null;
            return JoinRules.MayJoin(info != null, incompatible);
        }

        /// <summary>The session, for which Direct section shows.</summary>
        private DirectFacts FactsOf(Session session) => new DirectFacts
        {
            Active = session.Active,
            OnTcp = session.Backend == _tcp,
            Host = session.IsHost,
            TcpBusy = _tcp.InSession,
        };

        /// <summary>The router's mapping, for a host's internet row.</summary>
        private InternetFacts FactsOf(PortMapper map) => new InternetFacts
        {
            Mapped = map.Mapped,
            RouterIp = map.ExternalIp,
            RouterPort = map.ExternalPort,
            BehindAnotherNat = map.BehindAnotherNat,
            PublicIp = _publicIp,
            Port = _tcp.Port,
        };

        /// <summary>A click on a server's row, for the double click.</summary>
        private ClickFacts ClickOf(string address) => new ClickFacts
        {
            Address = address,
            Last = _lastClicked,
            Now = new Seconds(Time.unscaledTime),
            LastAt = new Seconds(_lastClick),
        };

        /// <summary>The server Connect joins, and the game we're in.</summary>
        private ConnectFacts ConnectOf(string addr, string password)
        {
            ServerInfo info = null;
            if (addr != null)
            {
                info = _browser.Get(addr).Info;
            }

            return new ConnectFacts
            {
                Address = addr,
                Known = info != null,
                Incompatible = info != null && info.Incompatibility != null,
                NeedsPassword = info != null && info.Password,
                PasswordGiven = password != null,
                InGame = _session.Active,
                Host = _session.IsHost,
            };
        }

        /// <summary>
        /// For the tests: Connect, as the browser's Connect button does.
        /// </summary>
        internal void JoinForTest(string addr) => Connect(addr, "");

        /// <summary>
        /// Joins the server, asking for its password first if it has one.
        /// </summary>
        private void Connect(string addr, string password = null)
        {
            ConnectPlan plan = JoinRules.Connect(ConnectOf(addr, password));
            if (plan.Step == ConnectStep.AskPassword)
            {
                _passwordFor = addr;
                _password = "";
                _focusField = true;
                _passwordWin.Show();
                return;
            }

            // Joining leaves the game we're in (a joiner's, Steam or direct;
            // hosts get no join buttons while hosting).
            if (plan.Step != ConnectStep.Join)
            {
                return;
            }

            if (plan.LeaveFirst)
            {
                Plugin.Log.LogInfo(
                    $"Joining {addr}: leaving the game we're in first");
                _session.Backend.Leave();
            }

            if (_tcp.InSession)
            {
                _tcp.Leave(); // still connecting, not in yet
            }

            _session.LastRefusal = null;
            _browser.Joined(addr);
            Plugin.Cfg.LastAddress.Value = addr;
            string sentPassword = password;
            if (sentPassword == null)
            {
                sentPassword = "";
            }

            _tcp.Join(addr, sentPassword);
            Action handler = OnJoining;
            if (handler != null)
            {
                handler();
            }

            CloseWindows();
        }

        private void OpenAddress()
        {
            if (string.IsNullOrEmpty(_address))
            {
                string address = Plugin.Cfg.LastAddress.Value;
                if (address == null)
                {
                    address = "";
                }

                _address = address;
            }

            _focusField = true;
            _addressWin.Show();
        }

        private void DrawAddress(Flow f)
        {
            _address =
                Ui.Field(f, "Address", _address, 64, name: "dwmp-address");
            Ui.Text(
                f,
                $"host or host:port (port {TcpBackend.DefaultPort} if you "
                    + "leave it out)",
                MenuSkin.Dim);
            _addressPassword =
                Ui.Field(f, "Password", _addressPassword, 32, password: true);
            if (_focusField)
            {
                _focusField = false;
                GUI.FocusControl("dwmp-address");
            }

            if (Ui.EnterPressed())
            {
                ConnectToAddress();
            }
        }

        private void AddressButtons(Rect r)
        {
            var b = Ui.Split(r, 120, 0, 80, 80);
            bool valid = TcpBackend.Normalize(_address) != null;
            if (Ui.Button(b[0], "Add to favorites", valid))
            {
                _browser.AddFavorite(_address);
                _selected = TcpBackend.Normalize(_address);
                SwitchTab(ServerBrowser.Tab.Favorites);
                _addressWin.Close();
                OpenServerBrowser();
            }

            if (Ui.Button(b[2], "Connect", valid))
            {
                ConnectToAddress();
            }

            if (Ui.Button(b[3], "Cancel"))
            {
                _addressWin.Close();
            }
        }

        private void ConnectToAddress()
        {
            string addr = TcpBackend.Normalize(_address);
            if (addr == null)
            {
                return;
            }

            string password = _addressPassword;
            if (password == null)
            {
                password = "";
            }

            Connect(addr, password);
        }

        private void DrawPassword(Flow f)
        {
            Ui.Text(f, $"{Name(_passwordFor)} needs a password.");
            var c = f.Row(Ui.RowH, 0);
            _password = Ui.Field(
                c[0], _password, 32, password: true, name: "dwmp-password");
            if (_focusField)
            {
                _focusField = false;
                GUI.FocusControl("dwmp-password");
            }

            if (Ui.EnterPressed())
            {
                string password = _password;
                if (password == null)
                {
                    password = "";
                }

                Connect(_passwordFor, password);
            }
        }

        private void PasswordButtons(Rect r)
        {
            var b = Ui.Split(r, 0, 80, 80);
            if (Ui.Button(b[1], "Join"))
            {
                string password = _password;
                if (password == null)
                {
                    password = "";
                }

                Connect(_passwordFor, password);
            }

            if (Ui.Button(b[2], "Cancel"))
            {
                _passwordWin.Close();
            }
        }

        private string Name(string addr)
        {
            if (addr == null)
            {
                return "";
            }

            ServerInfo info = _browser.Get(addr).Info;
            string name = null;
            if (info != null)
            {
                name = info.Name;
            }

            if (name is string n && n.Length > 0)
            {
                return n;
            }

            return addr;
        }
    }
}
