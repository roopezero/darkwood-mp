using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// The main menu's Start Game, in place of the game's profiles screen: one window to pick a save (or a new
    /// game), the new game's settings, and whether to host it.
    ///
    /// 1. Save: a list of the saved slots (the slot, the name you gave it, its day, chapter and difficulty, when it
    ///    was last played, and who has played it with you, from SaveBook), and New Game in the first free slot. With
    ///    every slot in use, New Game asks which save to overwrite (ConfirmDialog). A save can be renamed and deleted
    ///    (as the game's own Delete, asked first).
    /// 2. New game: the game's own choices (difficulty, skip the prologue, backer content); off for a save, where
    ///    they show what the save was started with.
    /// 3. Host a multiplayer game: Steam or Direct, your name, the server's name, port, password and most players,
    ///    and what new players start with. Off until the box is ticked.
    ///
    /// Start goes through the game's own code: a save loads as its Play button does (UI.initLoadGame), a new game
    /// starts as the profiles screen's new game does (the slot deleted, a new GameProfile, UI.initNewGame). With the
    /// box ticked the game hosts once the world has loaded (Plugin.HostWhenReady), as Multiplayer in the pause
    /// menu would.
    /// </summary>
    public sealed class StartGameWindow
    {
        private sealed class Slot
        {
            public int Id;
            public GameProfile Profile; // null: free (never used, deleted, or dead for good)
            public List<string> PlayedWith = new List<string>();
            public bool Free => Profile == null;
        }

        public static StartGameWindow Instance { get; private set; }

        private readonly UiWindow _win;
        private readonly List<Slot> _slots = new List<Slot>();
        private List<GameProfile> _profiles;
        private string _problem;     // the save list couldn't be read as the game would show it
        private int _choice = -1;    // a slot id, or 0 for New Game
        private int _overwrite;      // New Game with every slot in use: the slot to overwrite (asked first)
        private bool _listOpen;
        private string _name = "";
        private int _nameFor = -1;

        private GameProfile.Difficulty _difficulty = GameProfile.Difficulty.normal;
        private bool _skipPrologue;
        private bool _backer = true;
        private bool _host;

        public bool Open => _win.Open;

        public StartGameWindow()
        {
            Instance = this;
            float S() => PixelFont.Scale;
            _win = new UiWindow(0x0D41, "Start Game")
            {
                MinSize = new Vector2(380, 220),
                FitContent = true,
                DefaultRect = () => UiWindow.RightSide(470 * S(), 520 * S(), top: 0.04f),
                Body = DrawBody,
                Footer = DrawFooter,
                FooterHeight = 13 + 8,
                OnClose = CommitName,
            };
            MenuWindows.OpenStartGame = Show;
            MenuWindows.Register(() => _win.Open);
            MenuWindows.Draw += OnGUI;
        }

        public void Show()
        {
            Refresh();
            _host = false;
            _listOpen = false;
            _win.ResetPlace();
            _win.Show();
            Plugin.Log.LogInfo($"[start game] opened: {_slots.Count(s => !s.Free)} saves, {_slots.Count(s => s.Free)} free slots");
        }

        public void Close() => _win.Close();

        /// <summary>The game's profiles, as its profiles screen reads them (SaveManager.loadGameProfiles).</summary>
        private void Refresh()
        {
            _slots.Clear();
            _problem = null;
            var sm = Singleton<SaveManager>.Instance;
            var state = sm?.loadGameProfiles();
            bool any = state?.profiles != null && state.profiles.Any(p => p != null && p.Active);
            if (!any && sm != null && sm.CheckIfWorldFilesExist())
                _problem = "The game's list of saves is missing or damaged, but there are save files. The game's own profiles screen can restore it from a backup.";
            // Five slots, as the game always has; a slot that isn't active (or whose character died for good) is free.
            _profiles = new List<GameProfile>();
            int count = Math.Max(5, state?.profiles?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                var p = state?.profiles != null && i < state.profiles.Count ? state.profiles[i] : null;
                bool used = p != null && p.Active && !p.dead;
                _profiles.Add(used ? p : new GameProfile(i + 1, _Active: false, 0));
                var slot = new Slot { Id = i + 1, Profile = used ? p : null };
                if (used) slot.PlayedWith = SaveBook.PlayedWith(slot.Id);
                _slots.Add(slot);
            }
            if (_choice != 0 && !_slots.Any(s => s.Id == _choice && !s.Free)) _choice = -1;
            if (_choice < 0)
            {
                // The save played last, or New Game when there's none.
                var last = _slots.Where(s => !s.Free).OrderByDescending(s => SavedAt(s.Profile)).FirstOrDefault();
                _choice = last?.Id ?? 0;
            }
            _overwrite = 0;
            LoadName();
        }

        private static DateTime SavedAt(GameProfile p) => DateTime.TryParse(p?.timeSaved, out var t) ? t : DateTime.MinValue;

        private Slot Chosen => _choice > 0 ? _slots.FirstOrDefault(s => s.Id == _choice) : null;
        private Slot FreeSlot => _slots.FirstOrDefault(s => s.Free);
        /// <summary>The slot a new game goes in: the first free one, or the one chosen to overwrite.</summary>
        private int NewSlot => FreeSlot?.Id ?? _overwrite;

        private void Choose(int choice)
        {
            CommitName();
            _choice = choice;
            _overwrite = 0;
            _listOpen = false;
            LoadName();
        }

        private void LoadName()
        {
            _nameFor = _choice;
            _name = _choice > 0 ? SaveBook.NameOf(_choice) : "";
        }

        /// <summary>A save's new name is written when it's left (another choice, Start, closing the window).</summary>
        private void CommitName()
        {
            if (_nameFor > 0 && Chosen != null && _nameFor == _choice) SaveBook.SetName(_nameFor, _name);
        }

        // ---- drawing ------------------------------------------------------------------------------------------

        private void OnGUI()
        {
            if (!_win.Open) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && !ConfirmDialog.Open) { e.Use(); Close(); return; }
            _win.OnGUI();
        }

        private static float S => PixelFont.Scale;

        private static string Difficulty(GameProfile.Difficulty d) =>
            d == GameProfile.Difficulty.hard ? "Hard" : d == GameProfile.Difficulty.nightmare ? "Nightmare" : "Normal";

        private static string Summary(GameProfile p)
        {
            string s = $"Day {p.day}";
            if (p.chapter > 0) s += $", chapter {Mathf.Clamp(p.chapter, 1, 10)}";
            return s + ", " + Difficulty(p.difficulty).ToLowerInvariant();
        }

        /// <summary>One line for a save in the list.</summary>
        private static string Line(Slot s)
        {
            string name = SaveBook.NameOf(s.Id);
            string with = s.PlayedWith.Count == 0 ? "single-player" : "with " + string.Join(", ", s.PlayedWith);
            return $"Slot {s.Id}" + (name != "" ? $": {name}" : "") + $"  -  {Summary(s.Profile)}  -  {with}";
        }

        private string NewGameLine() =>
            FreeSlot != null ? $"New Game  (in slot {FreeSlot.Id})" : $"New Game  (every slot is in use: overwrite one)";

        private void DrawBody(Flow f)
        {
            Ui.Heading(f, "Save");
            if (_problem != null)
            {
                Ui.Text(f, _problem, MenuSkin.Warn);
                if (Ui.Button(f, "Open the game's profiles screen")) { Close(); Singleton<MainMenu>.Instance.displayProfilesMenu(); }
            }
            DrawList(f);
            if (_choice > 0) DrawSave(f, Chosen);
            else DrawNewSlot(f);

            Ui.Heading(f, "New game");
            DrawNewGame(f, _choice == 0);

            Ui.Heading(f, "Multiplayer");
            var hr = f.Next(Ui.RowH);
            bool canHost = Plugin.Instance != null && !Plugin.Instance.Session.Active;
            _host = Check(hr, _host && canHost, "Host a multiplayer game", canHost);
            if (!canHost) Ui.Text(f, "You're in a multiplayer game now. Leave it first (Join Game, Multiplayer).", MenuSkin.Dim);
            DrawHost(f, _host);
        }

        /// <summary>The save list: the chosen line, which opens the list under it (pushing the rest down).</summary>
        private void DrawList(Flow f)
        {
            var r = f.Next(Ui.RowH);
            bool hover = r.Contains(Event.current.mousePosition);
            if (GUI.Button(r, GUIContent.none, MenuSkin.Sunken)) _listOpen = !_listOpen;
            string text = _choice > 0 && Chosen != null ? Line(Chosen) : NewGameLine();
            float pad = 6 * S, arrow = 14 * S;
            Ui.Label(new Rect(r.x + pad, r.y, r.width - 2 * pad - arrow, r.height), PixelFont.Fit(text, r.width - 2 * pad - arrow), hover ? MenuSkin.Bright : MenuSkin.Text);
            Ui.Label(new Rect(r.xMax - pad - arrow, r.y, arrow, r.height), _listOpen ? "^" : "v", MenuSkin.Bright, TextAnchor.MiddleCenter);
            if (!_listOpen) return;

            var rows = _slots.Where(s => !s.Free).Select(s => (s.Id, Line(s))).ToList();
            rows.Add((0, NewGameLine()));
            float rowH = PixelFont.Line + 6 * S;
            var box = new Rect(r.x, f.Y - f.Gap + S, r.width, rows.Count * rowH + 4 * S);
            GUI.Box(box, GUIContent.none, MenuSkin.Sunken);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = new Rect(box.x + 2 * S, box.y + 2 * S + i * rowH, box.width - 4 * S, rowH);
                bool over = row.Contains(Event.current.mousePosition);
                bool chosen = rows[i].Item1 == _choice;
                if (Event.current.type == EventType.Repaint && (over || chosen)) MenuSkin.Selected.Draw(row, false, false, false, false);
                if (GUI.Button(row, GUIContent.none, MenuSkin.Plain)) Choose(rows[i].Item1);
                Ui.Label(new Rect(row.x + pad - 2 * S, row.y, row.width - 2 * pad, row.height), PixelFont.Fit(rows[i].Item2, row.width - 2 * pad), over || chosen ? MenuSkin.Bright : MenuSkin.Text);
            }
            f.Y = box.yMax + f.Gap;
        }

        private void DrawSave(Flow f, Slot s)
        {
            if (s == null) return;
            var p = s.Profile;
            var c = f.Row(Ui.RowH, 90, 0, 110);
            Ui.Label(c[0], "Name");
            _name = Ui.Field(c[1], _name, 40);
            if (Ui.Button(c[2], "Delete save..."))
                ConfirmDialog.Ask("Delete save", $"Delete the save in slot {s.Id} ({Summary(p)})? It can't be undone.", "Delete", () => Delete(s.Id));
            Info(f, "Played", $"{Summary(p)};  last saved {p.timeSaved}");
            Info(f, "Played with", s.PlayedWith.Count == 0 ? "nobody: single-player" : string.Join(", ", s.PlayedWith));
            if (!p.isCompatible)
                Ui.Text(f, $"This save is from an older version of the game ({p.versionText()}). Open it from the game's own profiles screen, which explains what can be done with it.", MenuSkin.Warn);
        }

        private static void Info(Flow f, string label, string text)
        {
            var lines = PixelFont.Wrap(text, f.Width - 94 * S);
            for (int i = 0; i < lines.Count; i++)
            {
                var c = Ui.Split(new Rect(f.Area.x, f.Y, f.Width, PixelFont.Line), 90, 0);
                if (i == 0) Ui.Label(c[0], label, MenuSkin.Dim);
                Ui.Label(c[1], lines[i], MenuSkin.Bright);
                f.Y += PixelFont.Line + S;
            }
            f.Y += f.Gap - S;
        }

        private void DrawNewSlot(Flow f)
        {
            var c = f.Row(Ui.RowH, 90, 0);
            Ui.Label(c[0], "Name");
            _name = Ui.Field(c[1], _name, 40);
            if (FreeSlot != null) return;
            Ui.Text(f, "Every save slot is in use. Choose a save to overwrite (it's deleted when the new game starts):", MenuSkin.Text);
            var used = _slots.Where(x => !x.Free).ToList();
            var widths = Enumerable.Repeat(0f, used.Count).ToArray();
            var b = f.Row(Ui.RowH, widths);
            for (int i = 0; i < used.Count; i++)
            {
                var s = used[i];
                if (Choice(b[i], $"Slot {s.Id}", _overwrite == s.Id, true) && _overwrite != s.Id)
                {
                    int id = s.Id;
                    string label = SaveBook.NameOf(id);
                    ConfirmDialog.Ask("Overwrite save", $"Start the new game in slot {id}{(label != "" ? $" ({label})" : "")}? Its save ({Summary(s.Profile)}) is deleted when the game starts.", "Overwrite", () => _overwrite = id);
                }
            }
            if (_overwrite > 0) Ui.Text(f, $"The new game goes in slot {_overwrite}; its save is deleted when you press Start.", MenuSkin.Warn);
        }

        private void DrawNewGame(Flow f, bool on)
        {
            // For a save: what it was started with, greyed out.
            var p = Chosen?.Profile;
            var diff = on || p == null ? _difficulty : p.difficulty;
            bool skip = on || p == null ? _skipPrologue : p.skippedPrologue;
            bool backer = on || p == null ? _backer : p.backerOnlyContent;
            Fade(on, () =>
            {
                var c = f.Row(Ui.RowH, 90, 0, 0, 0);
                Ui.Label(c[0], "Difficulty");
                foreach (var (i, d) in new[] { (1, GameProfile.Difficulty.normal), (2, GameProfile.Difficulty.hard), (3, GameProfile.Difficulty.nightmare) })
                    if (Choice(c[i], Difficulty(d), diff == d, on)) _difficulty = d;
                string about = diff == GameProfile.Difficulty.hard ? "A few lives; when they're gone, so is the save."
                    : diff == GameProfile.Difficulty.nightmare ? "One life: dying deletes the save."
                    : "Dying costs you some of what you carried, and the game goes on.";
                Ui.Text(f, about, MenuSkin.Dim);
                var k = f.Row(Ui.RowH, 0, 0);
                bool ns = Check(k[0], skip, "Skip the prologue", on);
                bool nb = Check(k[1], backer, "Backer content", on);
                if (on) { _skipPrologue = ns; _backer = nb; }
            });
        }

        private void DrawHost(Flow f, bool on)
        {
            var plugin = Plugin.Instance;
            var cfg = Plugin.Cfg;
            bool steamUp = plugin.SteamAvailable;
            bool steam = steamUp && cfg.HostWith.Value != "direct";
            Fade(on, () =>
            {
                var c = f.Row(Ui.RowH, 90, 0, 0);
                Ui.Label(c[0], "Connection");
                if (Choice(c[1], "Steam", steam, on && steamUp)) cfg.HostWith.Value = "steam";
                if (Choice(c[2], "Direct", !steam, on)) cfg.HostWith.Value = "direct";
                if (!steamUp) Ui.Text(f, plugin.SteamMissing ? "Steam isn't running (the GOG version, say): Direct only." : "Waiting for Steam...", MenuSkin.Dim);

                var n = f.Row(Ui.RowH, 90, 0);
                Ui.Label(n[0], "Your name");
                string name = Ui.Field(n[1], plugin.Session.LocalName, 32);
                if (on && name != plugin.Session.LocalName) plugin.SetPlayerName(name);

                Fade(on && !steam, () =>
                {
                    cfg.ServerName.Value = Ui.Field(f, "Server name", cfg.ServerName.Value, 48);
                    var pm = f.Row(Ui.RowH, 90, 70, 0, 90, 24, 30, 24);
                    Ui.Label(pm[0], "Port");
                    string port = Ui.Field(pm[1], cfg.DirectPort.Value.ToString(), 5);
                    if (int.TryParse(port, out int pn) && pn > 0 && pn <= 65535) cfg.DirectPort.Value = pn;
                    Ui.Label(pm[3], "Max players", null, TextAnchor.MiddleRight);
                    if (Ui.Button(pm[4], "-", on && !steam && cfg.MaxPlayers.Value > 2)) cfg.MaxPlayers.Value--;
                    Ui.Label(pm[5], cfg.MaxPlayers.Value.ToString(), MenuSkin.Bright, TextAnchor.MiddleCenter);
                    if (Ui.Button(pm[6], "+", on && !steam && cfg.MaxPlayers.Value < Protocol.MaxPlayers)) cfg.MaxPlayers.Value++;
                    cfg.Password.Value = Ui.Field(f, "Password", cfg.Password.Value, 32, password: true);
                });

                var ch = f.Row(Ui.RowH, 90, 0, 0);
                Ui.Label(ch[0], "New players");
                bool copy = cfg.NewCharacters.Value == "copy";
                if (Choice(ch[1], "Start fresh", !copy, on)) cfg.NewCharacters.Value = "fresh";
                if (Choice(ch[2], "Copy of you", copy, on)) cfg.NewCharacters.Value = "copy";
                Ui.Text(f, steam
                    ? "Friends join by your Steam invite, or Join Game in the Steam friends list. Hosting starts once your world has loaded."
                    : $"Friends connect on TCP port {cfg.DirectPort.Value} and the browser asks on UDP {cfg.DirectPort.Value}: forward both, or use a VPN (Tailscale, ZeroTier). Hosting starts once your world has loaded.",
                    MenuSkin.Dim);
            });
        }

        /// <summary>Draws a part greyed out and taking no input when <paramref name="on"/> is false.</summary>
        private static void Fade(bool on, Action draw)
        {
            bool enabled = GUI.enabled;
            var color = GUI.color;
            if (!on) { GUI.enabled = false; GUI.color = new Color(color.r, color.g, color.b, color.a * 0.45f); }
            try { draw(); }
            finally { GUI.enabled = enabled; GUI.color = color; }
        }

        /// <summary>One of a row of choices: the chosen one pressed in.</summary>
        private static bool Choice(Rect r, string text, bool chosen, bool enabled)
        {
            if (!chosen) return Ui.Button(r, text, enabled && GUI.enabled);
            GUI.Box(r, GUIContent.none, MenuSkin.Pressed);
            PixelFont.Draw(new Rect(r.x + S, r.y + S, r.width, r.height), text, MenuSkin.Bright, TextAnchor.MiddleCenter);
            return false;
        }

        private static bool Check(Rect r, bool on, string text, bool enabled)
        {
            if (enabled && GUI.enabled) return Ui.Check(r, on, text);
            float box = PixelFont.Line - 2 * S;
            var b = new Rect(r.x, r.y + (r.height - box) / 2, box, box);
            GUI.Box(b, GUIContent.none, MenuSkin.Sunken);
            if (on) GUI.Box(new Rect(b.x + 3 * S, b.y + 3 * S, b.width - 6 * S, b.height - 6 * S), GUIContent.none, MenuSkin.Line);
            Ui.Label(new Rect(b.xMax + 6 * S, r.y, r.width - box - 6 * S, r.height), text, MenuSkin.Dim);
            return on;
        }

        private void DrawFooter(Rect r)
        {
            var c = Ui.Split(r, 0, 110, 90);
            string why = CantStart();
            if (why != null) Ui.Label(c[0], PixelFont.Fit(why, c[0].width), MenuSkin.Dim);
            if (Ui.Button(c[1], _host ? "Start and host" : "Start", why == null)) Start();
            if (Ui.Button(c[2], "Cancel")) Close();
        }

        /// <summary>Why Start is off, or null.</summary>
        private string CantStart()
        {
            if (!WorldShare.MainMenuReady()) return "Wait for the main menu.";
            if (_choice > 0) return Chosen?.Profile?.isCompatible == false ? "This save needs the game's profiles screen." : null;
            return NewSlot > 0 ? null : "Choose a save to overwrite.";
        }

        // ---- starting ----------------------------------------------------------------------------------------

        internal void Start()
        {
            if (CantStart() != null) return;
            CommitName();
            var plugin = Plugin.Instance;
            bool host = _host && !plugin.Session.Active;
            bool steam = plugin.SteamAvailable && Plugin.Cfg.HostWith.Value != "direct";
            string name = _name;
            Core.profiles = _profiles;
            var mm = Singleton<MainMenu>.Instance;
            if (_choice > 0)
            {
                var p = Chosen.Profile;
                Plugin.Log.LogInfo($"[start game] continue slot {p.id} (day {p.day}){(host ? $", then host with {(steam ? "Steam" : "Direct")}" : "")}");
                mm.creatingProfile = false;
                Core.currentProfile = p;
                Close();
                Singleton<UI>.Instance.StartCoroutine(Singleton<UI>.Instance.initLoadGame());
            }
            else
            {
                int id = NewSlot;
                Plugin.Log.LogInfo($"[start game] new game in slot {id} ({Difficulty(_difficulty)}, skip prologue {_skipPrologue}, backer content {_backer}){(host ? $", then host with {(steam ? "Steam" : "Direct")}" : "")}");
                // As the profiles screen does for a free slot (MainMenu.displayProfile), then its Play (Button.playProfile).
                Core.currentProfile = _profiles[id - 1];
                Singleton<SaveManager>.Instance.deleteCurrentProfile(creatingNewProfile: true);
                mm.creatingProfile = true;
                var p = new GameProfile(id, _Active: false, 0) { difficulty = _difficulty, backerOnlyContent = _backer };
                Core.currentProfile = p;
                Singleton<Controller>.Instance.skipTutorial = _skipPrologue;
                Close();
                Singleton<UI>.Instance.StartCoroutine(Singleton<UI>.Instance.initNewGame());
                if (!string.IsNullOrWhiteSpace(name)) SaveBook.SetName(id, name);
            }
            if (host) plugin.HostWhenReady(steam);
        }

        // ---- the test ------------------------------------------------------------------------------------------

        /// <summary>
        /// "--dwmp-test-start-window": Start Game in the main menu's list opens this window. With --dwmp-sandbox DIR
        /// the saves are the copy in DIR (the record too: DIR/darkwoodmp-saves.json); --dwmp-screen WxH sizes the game's
        /// window first; --dwmp-shots DIR takes screenshots of it (a save, the list open, New Game, the host fields,
        /// Delete asked). "--dwmp-start N|new" then picks slot N or New Game and presses Start
        /// ("--dwmp-start-host direct|steam" ticks the host box, "--dwmp-start-name NAME" names the save). Logged "[start window]".
        /// </summary>
        internal static IEnumerator Test(string sandbox, string screen, string shots, string start, string hostWith, string name)
        {
            while (!WorldShare.MainMenuReady()) yield return null;
            yield return new WaitForSecondsRealtime(2f);
            if (screen != null)
            {
                var wh = screen.Split('x');
                Screen.SetResolution(int.Parse(wh[0]), int.Parse(wh[1]), FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(3f);
            }
            if (sandbox != null) SaveRedirect.Begin(sandbox);
            var w = Instance;
            MenuList.Click("Start Game");
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Log.LogInfo($"[start window] clicked Start Game, window open: {w.Open}");
            foreach (var s in w._slots)
                Plugin.Log.LogInfo($"[start window] slot {s.Id}: " + (s.Free ? "free" : Line(s)));
            if (!w.Open) yield break;

            string prefix = $"{Screen.width}x{Screen.height}-";
            IEnumerator Shot(string what)
            {
                if (shots == null) yield break;
                System.IO.Directory.CreateDirectory(shots);
                yield return new WaitForSecondsRealtime(0.7f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shots, prefix + what + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            int firstSave = w._slots.FirstOrDefault(s => !s.Free)?.Id ?? 0;
            if (shots != null)
            {
                yield return Shot("start-save");
                w._listOpen = true;
                yield return Shot("start-list");
                w.Choose(0);
                yield return Shot("start-new");
                w._host = true; Plugin.Cfg.HostWith.Value = "direct";
                yield return Shot("start-new-host");
                if (firstSave > 0)
                {
                    w.Choose(firstSave);
                    yield return Shot("start-save-host");
                    var s = w.Chosen;
                    ConfirmDialog.Ask("Delete save", $"Delete the save in slot {s.Id} ({Summary(s.Profile)})? It can't be undone.", "Delete", () => Plugin.Log.LogInfo("[start window] (not deleted)"));
                    yield return Shot("start-delete");
                    ConfirmDialog.Cancel();
                }
                w._host = false;
            }
            if (start == null)
            {
                w.Close();
                if (sandbox != null) SaveRedirect.End();
                Plugin.Log.LogInfo("[start window] done");
                yield break;
            }
            int slot = start == "new" ? 0 : int.Parse(start);
            w.Choose(slot);
            if (name != null) w._name = name;
            w._host = hostWith != null;
            if (hostWith != null) Plugin.Cfg.HostWith.Value = hostWith;
            yield return Shot("start-go");
            Plugin.Log.LogInfo($"[start window] pressing Start: {(slot == 0 ? $"new game in slot {w.NewSlot}" : $"slot {slot}")}, host {hostWith ?? "no"}; can start: {w.CantStart() ?? "yes"}");
            w.Start();
        }

        private void Delete(int id)
        {
            var slot = _profiles.FirstOrDefault(p => p.id == id);
            if (slot == null) return;
            Plugin.Log.LogInfo($"[start game] delete slot {id}");
            // As the profiles screen's Delete (MainMenu.confirmBoxYes, deleteProfile).
            Core.profiles = _profiles;
            Core.currentProfile = slot;
            Singleton<SaveManager>.Instance.deleteCurrentProfile();
            if (_choice == id) _choice = -1;
            _nameFor = -1;
            Refresh();
        }
    }
}
