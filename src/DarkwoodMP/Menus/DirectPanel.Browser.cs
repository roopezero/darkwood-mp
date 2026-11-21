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
    }
}
