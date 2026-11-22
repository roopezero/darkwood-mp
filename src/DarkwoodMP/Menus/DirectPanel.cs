using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// Direct connections in the co-op windows: the Multiplayer window's Direct
    /// section (hosting, or the buttons that open the browser), and the windows
    /// it opens, after Half-Life's: the server browser (Favorites, History,
    /// LAN, with sortable columns), "Connect to address" and the password
    /// prompt.
    /// </summary>
    public sealed partial class DirectPanel
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
    }
}
