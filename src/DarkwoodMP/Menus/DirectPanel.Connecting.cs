using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    public sealed partial class DirectPanel
    {
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
