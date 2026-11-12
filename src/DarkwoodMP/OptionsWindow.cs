using System;
using System.Collections.Generic;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP
{
    /// <summary>
    /// The Options window, in place of the game's options screen (MenuWindows.OpenOptions: "Options" in the main menu
    /// and the pause menu). Tabs as the server browser's: Video, Audio, Game, Controls, Multiplayer. Every setting the
    /// game's screen had is here, read from and written to the game's own store (GameSettings, which is Unity's
    /// PlayerPrefs; Rewired's key maps through ReInput.userDataStore, also PlayerPrefs), and applied with the game's
    /// own calls, as its screen did:
    /// - Resolution and full screen change at once and ask "Keep these settings?", going back after 15 seconds
    ///   without an answer (the game's resolution box, with a countdown).
    /// - Brightness changes at once (VideoMenu.setGamma), as in the game's brightness box.
    /// - Post-processing, light quality, V-Sync, camera zoom, language and the two volumes wait for Apply (the
    ///   volumes are heard at once, as the game's sliders did); Revert, or closing and choosing Discard, puts them
    ///   back. A new language needs Darkwood started again, so applying one asks to quit, as the game's restart box.
    /// - Controls: the game's switches change at once. Keys and mouse buttons are changed by clicking one and pressing
    ///   the new one (Rewired's InputMapper, set up as the game's ControlsMenu: a key that another action has is taken
    ///   from it, unless that one can't be changed; Escape cancels). Controller buttons are shown; the game doesn't
    ///   let you change them. "Reset to defaults" is the game's revertControls.
    /// - Multiplayer: your name (the mod's General.PlayerName) and the hosting settings from the mod's config.
    /// </summary>
    public static class OptionsWindow
    {
        private enum Tab { Video, Audio, Game, Controls, Multiplayer }
        private static readonly string[] TabNames = { "Video", "Audio", "Game", "Controls", "Multiplayer" };

        private static UiWindow _win;
        private static Tab _tab;

        // What the tabs show for the settings that wait for Apply; the game's stored values when nothing is changed.
        private static int _postFx, _light, _vsync, _zoom;
        private static string _lang;
        private static float _music, _sound;
        private static bool _gammaChanged;

        private static readonly string[] LangCodes = { "EN", "PL", "RU", "ES", "DE", "PT_BR", "IT", "TR", "ZH", "HU", "FR" };
        private static readonly string[] LangNames = { "English", "Polish", "Russian", "Spanish", "German", "Portuguese (Brazil)", "Italian", "Turkish", "Chinese", "Hungarian", "French" };
        private static readonly string[] HighLow = { "High", "Low" };   // PostFXQuality: 0 high, 1 low
        private static readonly string[] LowHigh = { "Low", "High" };   // LightQuality: 0 low, 1 high
        private static readonly string[] AimAssist = { "Off", "Low", "High" };
        private static readonly string[] NewChars = { "fresh", "copy" };
        private static readonly string[] NewCharNames = { "Nothing, beside you", "A copy of your character" };

        private const float KeepSeconds = 15f;

        public static bool IsOpen => _win != null && _win.Open;

        public static void Init()
        {
            float S() => PixelFont.Scale;
            _win = new UiWindow(0x0D50, "Options")
            {
                MinSize = new Vector2(460, 300),
                DefaultRect = () => UiWindow.RightSide(600 * S(), 440 * S()),
                Body = DrawBody,
                Footer = DrawFooter,
                FooterHeight = 13 + 8,
                Overlay = DrawDrop,
                OnClose = OnCloseBox,
            };
            MenuWindows.OpenOptions = Open;
            MenuWindows.Register(() => IsOpen);
            MenuWindows.Draw += OnGUI;
        }

        public static void Open()
        {
            Load();
            _rows = null;
            _status = null;
            _drop = null;
            _win.ResetPlace();
            _win.Show();
            Plugin.Log.LogInfo("[options] opened");
        }

        /// <summary>Close as Close does: asks first when something is changed and not applied.</summary>
        public static void RequestClose()
        {
            if (!IsOpen) return;
            if (Pending)
                ConfirmDialog.Ask("Options", "Apply the changes you made?", "Apply", () => { if (Apply()) CloseNow(); }, "Discard", () => { Revert(); CloseNow(); });
            else CloseNow();
        }

        private static void CloseNow()
        {
            _win.OnClose = null;
            _win.Close();
            _win.OnClose = OnCloseBox;
            Closed();
        }

        private static void OnCloseBox()
        {
            // The close box has already closed it: open again to ask, if there's something to ask about.
            if (Pending) { _win.Show(); RequestClose(); }
            else Closed();
        }

        private static void Closed()
        {
            StopMapping(null);
            _drop = null;
            if (_gammaChanged) { _gammaChanged = false; GameSettings.Save(); }
            Plugin.Log.LogInfo("[options] closed");
        }

        // ---- the game's settings -------------------------------------------------------------------

        private static MainMenu Menu => Singleton<MainMenu>.Instance;

        private static void Load()
        {
            _postFx = GameSettings.GetInt("PostFXQuality");
            _light = GameSettings.GetInt("LightQuality");
            _vsync = GameSettings.GetInt("Vsync");
            _zoom = GameSettings.GetInt("CameraZoom");
            _lang = GameSettings.GetString("LanguageCode");
            _music = GameSettings.GetFloat("MusicVolume");
            _sound = GameSettings.GetFloat("SoundVolume");
        }

        /// <summary>Something waits for Apply.</summary>
        public static bool Pending =>
            _postFx != GameSettings.GetInt("PostFXQuality") || _light != GameSettings.GetInt("LightQuality") ||
            _vsync != GameSettings.GetInt("Vsync") || _zoom != GameSettings.GetInt("CameraZoom") ||
            _lang != GameSettings.GetString("LanguageCode") ||
            !Mathf.Approximately(_music, GameSettings.GetFloat("MusicVolume")) || !Mathf.Approximately(_sound, GameSettings.GetFloat("SoundVolume"));

        /// <summary>
        /// Apply what waits, as MainMenu.applyOptionsSettings does. A new language asks to quit first (false: not
        /// applied yet, the answer does it).
        /// </summary>
        public static bool Apply()
        {
            if (_lang != GameSettings.GetString("LanguageCode"))
            {
                string lost = Singleton<WorldGenerator>.Instance != null ? " What you did since you last saved is lost." : "";
                ConfirmDialog.Ask("Language", "The new language is used from when Darkwood starts again. Apply the changes and quit now?" + lost,
                    "Quit", () => { ApplyNow(); Core.quitGame(); }, "Not now",
                    () => { _lang = GameSettings.GetString("LanguageCode"); });
                return false;
            }
            ApplyNow();
            return true;
        }

        private static void ApplyNow()
        {
            var vm = Menu != null ? Menu.VideoMenu : null;
            var cam = Singleton<CamMain>.Instance;
            if (_postFx != GameSettings.GetInt("PostFXQuality"))
            {
                GameSettings.SetInt("PostFXQuality", _postFx);
                if (vm != null) { vm.postFXQuality = (VideoMenu.PostFXQuality)_postFx; vm.setPostFXQuality(); }
                if (cam != null) cam.setRenderTargets();
            }
            if (_light != GameSettings.GetInt("LightQuality"))
            {
                GameSettings.SetInt("LightQuality", _light);
                if (vm != null) vm.destLightQuality = _light;
                if (cam != null) cam.setRenderTargets();
            }
            if (_vsync != GameSettings.GetInt("Vsync"))
            {
                GameSettings.SetInt("Vsync", _vsync);
                if (vm != null) vm.destVsync = _vsync;
                QualitySettings.vSyncCount = _vsync;
            }
            if (_zoom != GameSettings.GetInt("CameraZoom"))
            {
                GameSettings.SetInt("CameraZoom", _zoom);
                if (vm != null) vm.destCameraZoom = _zoom;
                Singleton<Controller>.Instance?.refreshZoom();
            }
            if (!Mathf.Approximately(_music, GameSettings.GetFloat("MusicVolume"))) GameSettings.SetFloat("MusicVolume", _music);
            if (!Mathf.Approximately(_sound, GameSettings.GetFloat("SoundVolume"))) GameSettings.SetFloat("SoundVolume", _sound);
            if (!string.IsNullOrEmpty(_lang) && _lang != GameSettings.GetString("LanguageCode"))
            {
                GameSettings.SetString("LanguageCode", _lang);
                if (vm != null) vm.destLanguageCode = _lang;
            }
            _gammaChanged = false;
            GameSettings.Save();
            Plugin.Log.LogInfo($"[options] applied: post-fx {_postFx}, lights {_light}, vsync {_vsync}, zoom {_zoom}, music {_music:0.00}, sound {_sound:0.00}, language {_lang}");
        }

        /// <summary>Back to the stored values (the volumes heard as they were), as UI.revertOptionsSettings.</summary>
        public static void Revert()
        {
            Load();
            SetVolume(music: true, _music);
            SetVolume(music: false, _sound);
        }

        /// <summary>A volume, heard at once (the game's slider, which sets the mixer); stored on Apply.</summary>
        internal static void SetVolume(bool music, float v)
        {
            var am = Menu != null ? Menu.AudioMenu : null;
            var slider = am == null ? null : music ? am.musicSlider : am.soundSlider;
            if (slider != null) v = Mathf.Clamp(v, slider.minValue, slider.maxValue);
            if (music) _music = v; else _sound = v;
            if (slider != null) slider.Magnitude = v;
            else if (am != null) { if (music) am.setMusicVolume(v); else am.setSoundVolume(v); }
        }

        private static void SetGamma(float v)
        {
            var g = Menu != null ? Menu.gammaSlider : null;
            if (g != null) g.Magnitude = v; // VideoMenu.setGamma: the picture and GameSettings' "Gamma"
            else if (Menu != null) Menu.VideoMenu.setGamma(v);
            _gammaChanged = true;
        }

        /// <summary>A new resolution or full screen, at once; kept only if the player says so within 15 seconds (MainMenu.confirmBoxYes/No).</summary>
        private static void ChangeScreen(int w, int h, bool full)
        {
            int ow = GameSettings.GetInt("ScreenWidth"), oh = GameSettings.GetInt("ScreenHeight");
            bool ofull = GameSettings.GetInt("Fullscreen") == 1;
            if (ow <= 0 || oh <= 0) { ow = Screen.width; oh = Screen.height; }
            Screen.SetResolution(w, h, full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            Plugin.Log.LogInfo($"[options] screen {w}x{h} {(full ? "full screen" : "window")}, asking to keep it");
            AskKeep($"The screen is now {w}x{h}, {(full ? "full screen" : "in a window")}.", () =>
            {
                GameSettings.SetInt("ScreenWidth", w);
                GameSettings.SetInt("ScreenHeight", h);
                GameSettings.SetInt("Fullscreen", full ? 1 : 0);
                if (Menu != null) Menu.VideoMenu.destFulscreen = full ? 1 : 0;
                GameSettings.Save();
                Plugin.Log.LogInfo("[options] screen kept");
            }, () =>
            {
                Screen.SetResolution(ow, oh, ofull ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
                Plugin.Log.LogInfo($"[options] screen back to {ow}x{oh}");
            });
        }

        internal static void AskKeep(string text, Action keep, Action back) =>
            ConfirmDialog.Ask("Keep these settings?", text, "Keep", keep, "Go back", back, timeout: KeepSeconds);

        private static void SetSetting(string key, int v)
        {
            GameSettings.SetInt(key, v);
            GameSettings.Save();
            Plugin.Log.LogInfo($"[options] {key} = {v}");
        }

        // ---- drawing -------------------------------------------------------------------------------

        private static void OnGUI()
        {
            if (!IsOpen) return;
            var e = Event.current;
            // A click outside the window closes an open list (the window doesn't get it).
            if (_drop != null && e.type == EventType.MouseDown && !_win.Rect.Contains(e.mousePosition)) _drop = null;
            _win.OnGUI();
        }

        /// <summary>Every frame (Plugin.Update): Escape, and the key mapping's steps that wait for a frame.</summary>
        public static void Update()
        {
            if (!IsOpen) return;
            UpdateMapping();
            if (Input.GetKeyDown(KeyCode.Escape) && !ConfirmDialog.Open && Time.frameCount != _escTaken)
            {
                if (_drop != null) _drop = null;
                else RequestClose();
            }
        }

        private static float S => PixelFont.Scale;
        private static float LabelW => 150;

        private static void DrawBody(Flow f)
        {
            var tabs = Ui.Split(new Rect(f.Area.x, f.Y, 92 * TabNames.Length * S + 4 * S * (TabNames.Length - 1), Ui.RowH), 92, 92, 92, 92, 92);
            for (int i = 0; i < TabNames.Length; i++)
                if (Ui.Tab(tabs[i], TabNames[i], (int)_tab == i)) SwitchTab((Tab)i);
            f.Y += Ui.RowH;
            var panel = new Rect(f.Area.x, f.Y, f.Width, Mathf.Max(80 * S, f.Area.height - (f.Y - f.Area.y)));
            GUI.Box(panel, GUIContent.none, MenuSkin.Sunken);
            var inner = new Flow(new Rect(panel.x + 10 * S, panel.y + 8 * S, panel.width - 20 * S, panel.height - 16 * S));
            switch (_tab)
            {
                case Tab.Video: DrawVideo(inner); break;
                case Tab.Audio: DrawAudio(inner); break;
                case Tab.Game: DrawGame(inner); break;
                case Tab.Controls: DrawControls(inner); break;
                case Tab.Multiplayer: DrawMultiplayer(inner); break;
            }
            f.Y = panel.yMax;
        }

        private static void SwitchTab(Tab t)
        {
            if (t == _tab) return;
            StopMapping(null);
            _drop = null;
            _tab = t;
        }

        private static Rect[] Setting(Flow f, string label)
        {
            var c = f.Row(Ui.RowH, LabelW, 0);
            Ui.Label(c[0], label);
            return c;
        }

        private static void DrawVideo(Flow f)
        {
            var res = Core.getResolutions();
            var names = new List<string>();
            int cur = -1;
            for (int i = 0; i < res.Count; i++)
            {
                names.Add($"{res[i].width}x{res[i].height}");
                if (res[i].width == Screen.width && res[i].height == Screen.height) cur = i;
            }
            if (cur < 0) { names.Add($"{Screen.width}x{Screen.height}"); cur = names.Count - 1; }
            var c = Setting(f, "Resolution");
            Choice(Ui.Split(c[1], 160, 0)[0], "res", names.ToArray(), cur, i =>
            {
                if (i < res.Count && (res[i].width != Screen.width || res[i].height != Screen.height)) ChangeScreen(res[i].width, res[i].height, Screen.fullScreen);
            });
            c = Setting(f, "Full screen");
            bool full = Screen.fullScreen;
            if (Ui.Check(Ui.Split(c[1], 24, 0)[0], full, "") != full) ChangeScreen(Screen.width, Screen.height, !full);
            c = Setting(f, "Brightness");
            var g = Menu != null ? Menu.gammaSlider : null;
            float gmin = g != null ? g.minValue : 0.2f, gmax = g != null ? g.maxValue : 1.2f;
            float gamma = GameSettings.GetFloat("Gamma");
            var gc = Ui.Split(c[1], 0, 44);
            float ng = Slider(gc[0], gamma, gmin, gmax);
            if (!Mathf.Approximately(ng, gamma)) SetGamma(ng);
            Ui.Label(gc[1], Percent(gamma, gmin, gmax), MenuSkin.Bright, TextAnchor.MiddleRight);

            f.Space(6);
            c = Setting(f, "Post-processing");
            Choice(Ui.Split(c[1], 160, 0)[0], "postfx", HighLow, _postFx, i => _postFx = i);
            c = Setting(f, "Light quality");
            Choice(Ui.Split(c[1], 160, 0)[0], "light", LowHigh, _light, i => _light = i);
            c = Setting(f, "V-Sync");
            _vsync = Ui.Check(Ui.Split(c[1], 24, 0)[0], _vsync == 1, "") ? 1 : 0;
            f.Space(6);
            Ui.Text(f, "Resolution, full screen and brightness change at once. The others wait for Apply.", MenuSkin.Dim);
        }

        private static void DrawAudio(Flow f)
        {
            var am = Menu != null ? Menu.AudioMenu : null;
            for (int k = 0; k < 2; k++)
            {
                bool music = k == 0;
                var slider = am == null ? null : music ? am.musicSlider : am.soundSlider;
                float min = slider != null ? slider.minValue : 0f, max = slider != null ? slider.maxValue : 1f;
                float v = music ? _music : _sound;
                var c = Setting(f, music ? "Music volume" : "Sound volume");
                var cc = Ui.Split(c[1], 0, 44);
                float nv = Slider(cc[0], v, min, max);
                if (!Mathf.Approximately(nv, v)) SetVolume(music, nv);
                Ui.Label(cc[1], Percent(music ? _music : _sound, min, max), MenuSkin.Bright, TextAnchor.MiddleRight);
            }
            f.Space(6);
            Ui.Text(f, "You hear a new volume at once. Apply keeps it.", MenuSkin.Dim);
        }

        private static void DrawGame(Flow f)
        {
            var c = Setting(f, "Language");
            int li = Array.IndexOf(LangCodes, _lang);
            Choice(Ui.Split(c[1], 200, 0)[0], "lang", LangNames, li, i => _lang = LangCodes[i]);
            c = Setting(f, "Camera zoom");
            _zoom = Ui.Check(c[1], _zoom == 1, "Closer view (always on above 1920 pixels wide)") ? 1 : 0;
            f.Space(6);
            var cfg = Plugin.Cfg;
            c = Setting(f, "Studio logo");
            cfg.SkipIntro.Value = Ui.Check(c[1], cfg.SkipIntro.Value, "Skip it at start (about 6 seconds sooner)");
            f.Space(6);
            Ui.Text(f, "The language and camera zoom wait for Apply; a new language is used after a restart.", MenuSkin.Dim);
        }

        private static void DrawMultiplayer(Flow f)
        {
            var cfg = Plugin.Cfg;
            var session = Plugin.Instance != null ? Plugin.Instance.Session : null;
            bool inGame = session != null && session.Active;
            var c = Setting(f, "Your name");
            if (inGame) Ui.Label(c[1], session.LocalName, MenuSkin.Bright);
            else
            {
                string n = Ui.Field(c[1], cfg.PlayerName.Value, 32);
                if (n != cfg.PlayerName.Value) Plugin.Instance.SetPlayerName(n);
            }
            string shown = session != null ? session.LocalName : "";
            Ui.Text(f, inGame ? "You can change it when you're in no game." : $"Others see you as {shown}. Left empty, it's your Steam name, or your computer's user name.", MenuSkin.Dim);

            Ui.Heading(f, "Hosting");
            c = Setting(f, "Server name");
            cfg.ServerName.Value = Ui.Field(c[1], cfg.ServerName.Value, 48);
            c = Setting(f, "Port");
            string port = Ui.Field(Ui.Split(c[1], 80, 0)[0], cfg.DirectPort.Value.ToString(), 5);
            if (int.TryParse(port, out int p) && p > 0 && p <= 65535) cfg.DirectPort.Value = p;
            c = Setting(f, "Password");
            cfg.Password.Value = Ui.Field(c[1], cfg.Password.Value, 32, password: true);
            c = Setting(f, "Max players");
            var m = Ui.Split(c[1], 24, 30, 24, 0);
            if (Ui.Button(m[0], "-", cfg.MaxPlayers.Value > 2)) cfg.MaxPlayers.Value--;
            Ui.Label(m[1], cfg.MaxPlayers.Value.ToString(), MenuSkin.Bright, TextAnchor.MiddleCenter);
            if (Ui.Button(m[2], "+", cfg.MaxPlayers.Value < Net.Protocol.MaxPlayers)) cfg.MaxPlayers.Value++;
            c = Setting(f, "New players get");
            Choice(Ui.Split(c[1], 220, 0)[0], "newchars", NewCharNames, Array.IndexOf(NewChars, cfg.NewCharacters.Value), i => cfg.NewCharacters.Value = NewChars[i]);
            c = Setting(f, "World to join");
            cfg.LiveWorld.Value = Ui.Check(c[1], cfg.LiveWorld.Value, "As it is now (off: as you last saved it)");
            c = Setting(f, "");
            cfg.SnapshotOffThread.Value = Ui.Check(c[1], cfg.SnapshotOffThread.Value, "Write it for them in the background");
            Ui.Text(f, "These are used the next time you host.", MenuSkin.Dim);
        }

        private static void DrawFooter(Rect r)
        {
            var b = Ui.Split(r, 130, 0, 80, 80, 80);
            if (_tab == Tab.Controls && Ui.Button(b[0], "Reset to defaults"))
                ConfirmDialog.Ask("Reset controls", "Put every key and mouse button back as the game has them, and the switches above to theirs?", "Reset", ResetControls);
            bool pending = Pending;
            if (Ui.Button(b[2], "Apply", pending)) Apply();
            if (Ui.Button(b[3], "Revert", pending)) Revert();
            if (Ui.Button(b[4], "Close")) RequestClose();
        }

        private static string Percent(float v, float min, float max) => $"{Mathf.RoundToInt(Mathf.InverseLerp(min, max, v) * 100)}%";

        // ---- a slider ------------------------------------------------------------------------------

        private static float Slider(Rect r, float v, float min, float max)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            float thumbW = 8 * S;
            float From(Vector2 m) => Mathf.Lerp(min, max, Mathf.Clamp01((m.x - r.x - thumbW / 2) / Mathf.Max(1f, r.width - thumbW)));
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; v = From(e.mousePosition); e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) { v = From(e.mousePosition); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
                case EventType.Repaint:
                    // Held: follow the mouse itself, since slow movements send no MouseDrag (see UiWindow.MouseNow).
                    if (GUIUtility.hotControl == id) v = From(GUIUtility.ScreenToGUIPoint(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)));
                    float t = Mathf.InverseLerp(min, max, v);
                    var track = new Rect(r.x, r.center.y - 3 * S, r.width, 6 * S);
                    GUI.Box(track, GUIContent.none, MenuSkin.Sunken);
                    var fill = new Rect(track.x + 2 * S, track.y + 2 * S, Mathf.Max(0, (track.width - 4 * S) * t), track.height - 4 * S);
                    GUI.Box(fill, GUIContent.none, MenuSkin.Selected);
                    var thumb = new Rect(r.x + t * (r.width - thumbW), r.y + 3 * S, thumbW, r.height - 6 * S);
                    GUI.Box(thumb, GUIContent.none, MenuSkin.Button);
                    break;
            }
            return v;
        }

        // ---- a dropdown list -----------------------------------------------------------------------

        private sealed class Drop
        {
            public string Key;
            public string[] Options;
            public int Index, First = -1;
            public Action<int> Pick;
            public Vector2 Below; // the button's bottom left corner, on the screen
            public float Top, Width;
        }

        private static Drop _drop;

        /// <summary>A button showing the chosen one of <paramref name="options"/>; a click opens the list under it.</summary>
        private static void Choice(Rect r, string key, string[] options, int index, Action<int> pick)
        {
            bool open = _drop != null && _drop.Key == key;
            bool testOpen = _testDropKey == key && Event.current.type == EventType.Repaint;
            if (testOpen) { _testDropKey = null; open = false; }
            if (Ui.Button(r, "") || testOpen)
            {
                if (open) _drop = null;
                else _drop = new Drop
                {
                    Key = key, Options = options, Index = index, Pick = pick, Width = r.width,
                    Below = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.yMax)),
                    Top = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y)).y,
                };
            }
            string text = index >= 0 && index < options.Length ? options[index] : "";
            bool hover = r.Contains(Event.current.mousePosition);
            Ui.Label(new Rect(r.x + 6 * S, r.y, r.width - 20 * S, r.height), PixelFont.Fit(text, r.width - 22 * S), hover || open ? MenuSkin.Bright : MenuSkin.Text);
            DrawArrow(new Rect(r.xMax - 14 * S, r.y, 9 * S, r.height), hover || open ? MenuSkin.Bright : MenuSkin.Text);
        }

        /// <summary>A small triangle pointing down.</summary>
        private static void DrawArrow(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            float y = r.center.y - 2 * S;
            for (int i = 0; i < 4; i++)
                GUI.DrawTexture(new Rect(r.x + i * S, y + i * S, r.width - 2 * i * S, S), MenuSkin.White);
            GUI.color = old;
        }

        /// <summary>The open list, over everything in the window (UiWindow.Overlay): no IMGUI controls, events by hand.</summary>
        private static void DrawDrop()
        {
            if (_drop == null) return;
            var e = Event.current;
            float rowH = PixelFont.Line + 5 * S;
            var below = GUIUtility.ScreenToGUIPoint(_drop.Below);
            float top = GUIUtility.ScreenToGUIPoint(new Vector2(_drop.Below.x, _drop.Top)).y;
            int n = _drop.Options.Length;
            int fitDown = Mathf.FloorToInt((_win.Rect.height - below.y - 8 * S) / rowH);
            int fitUp = Mathf.FloorToInt((top - PixelFont.Line - 12 * S) / rowH);
            bool up = fitDown < Mathf.Min(n, 6) && fitUp > fitDown;
            int vis = Mathf.Clamp(up ? fitUp : fitDown, 1, n);
            float h = vis * rowH + 4 * S;
            var box = new Rect(below.x, up ? top - h : below.y, _drop.Width, h);
            if (_drop.First < 0) _drop.First = Mathf.Clamp(_drop.Index - vis / 2, 0, Mathf.Max(0, n - vis));
            _drop.First = Mathf.Clamp(_drop.First, 0, Mathf.Max(0, n - vis));
            int Under(Vector2 m) => box.Contains(m) ? _drop.First + Mathf.FloorToInt((m.y - box.y - 2 * S) / rowH) : -1;

            switch (e.type)
            {
                case EventType.MouseDown:
                {
                    int i = Under(e.mousePosition);
                    var pick = _drop.Pick;
                    _drop = null;
                    e.Use();
                    if (i >= 0 && i < n) pick(i);
                    return;
                }
                case EventType.ScrollWheel:
                    if (box.Contains(e.mousePosition)) { _drop.First += e.delta.y > 0 ? 1 : -1; e.Use(); }
                    return;
                case EventType.Repaint:
                {
                    // Opaque, so the settings under it don't show through.
                    var old = GUI.color;
                    GUI.color = new Color(0.09f, 0.09f, 0.09f, 1f);
                    GUI.DrawTexture(box, MenuSkin.White);
                    GUI.color = old;
                    GUI.Box(box, GUIContent.none, MenuSkin.Window);
                    int hover = Under(e.mousePosition);
                    for (int k = 0; k < vis && _drop.First + k < n; k++)
                    {
                        int i = _drop.First + k;
                        var rr = new Rect(box.x + 2 * S, box.y + 2 * S + k * rowH, box.width - 4 * S, rowH);
                        if (i == hover) GUI.Box(rr, GUIContent.none, MenuSkin.Selected);
                        Ui.Label(new Rect(rr.x + 4 * S, rr.y, rr.width - 12 * S, rr.height), PixelFont.Fit(_drop.Options[i], rr.width - 14 * S),
                            i == hover || i == _drop.Index ? MenuSkin.Bright : MenuSkin.Text);
                    }
                    if (vis < n)
                    {
                        // Where the shown part is in the list: a thin bar along the right edge.
                        float bh = (box.height - 4 * S) * vis / n, by = box.y + 2 * S + (box.height - 4 * S) * _drop.First / n;
                        GUI.Box(new Rect(box.xMax - 5 * S, by, 3 * S, bh), GUIContent.none, MenuSkin.Line);
                    }
                    return;
                }
            }
        }

        // ---- controls ------------------------------------------------------------------------------

        private sealed class Binding
        {
            public RW.InputAction Action;
            public RW.AxisRange Range;
            public string Label;
            public readonly string[] Keys = new string[3];   // keyboard, mouse, controller; null: none there
            public readonly int[] MapIds = { -1, -1, -1 };    // the ActionElementMap shown in each column
            public readonly bool[] Allowed = new bool[3];     // the game lists this action in that column
        }

        private static readonly string[] Columns = { "Keyboard", "Mouse", "Controller" };
        private static readonly RW.ControllerType[] ColumnTypes = { RW.ControllerType.Keyboard, RW.ControllerType.Mouse, RW.ControllerType.Joystick };
        private static List<Binding> _rows;
        private static Vector2 _listScroll;
        private static string _status;
        private static bool _statusWarn;

        private static RW.InputMapper _mapper;
        private static Binding _mapRow;
        private static int _mapCol;
        private static bool _startWhenReleased;
        private static RW.InputMapper.ConflictFoundEventData _conflict;
        private static int _escTaken;

        /// <summary>The player's map for a kind of controller, as ControlsMenu.GetControllerMap.</summary>
        private static RW.ControllerMap Map(RW.ControllerType type)
        {
            var p = Singleton<InputScript>.Instance != null ? Singleton<InputScript>.Instance.rewiredPlayer : null;
            if (p == null) return null;
            int id = 0;
            if (type == RW.ControllerType.Joystick && Singleton<Globals>.Instance != null && Singleton<Globals>.Instance.currentJoystick != null)
                id = Singleton<Globals>.Instance.currentJoystick.id;
            return p.controllers.maps.GetMap(type, id, "Default", "Default");
        }

        /// <summary>The rows of the game's controls page (ControlsMenu.populate and CreateUIRow), with the same columns left out.</summary>
        private static void BuildRows()
        {
            _rows = new List<Binding>();
            if (!RW.ReInput.isReady) return;
            var maps = new RW.ControllerMap[3];
            for (int c = 0; c < 3; c++) maps[c] = Map(ColumnTypes[c]);
            foreach (var a in RW.ReInput.mapping.UserAssignableActionsInCategory(0, true))
            {
                if (a.type == RW.InputActionType.Axis)
                {
                    AddRow(maps, a, RW.AxisRange.Full, a.descriptiveName);
                    AddRow(maps, a, RW.AxisRange.Positive, !string.IsNullOrEmpty(a.positiveDescriptiveName) ? a.positiveDescriptiveName : a.descriptiveName + " +");
                    AddRow(maps, a, RW.AxisRange.Negative, !string.IsNullOrEmpty(a.negativeDescriptiveName) ? a.negativeDescriptiveName : a.descriptiveName + " -");
                }
                else if (a.type == RW.InputActionType.Button) AddRow(maps, a, RW.AxisRange.Positive, a.descriptiveName);
            }
        }

        private static void AddRow(RW.ControllerMap[] maps, RW.InputAction a, RW.AxisRange range, string label)
        {
            if (a.descriptiveName == "Attack" || a.descriptiveName == "Context Menu" || a.descriptiveName == "RT") return;
            string name = range == RW.AxisRange.Full ? a.descriptiveName : range == RW.AxisRange.Positive ? a.positiveDescriptiveName : a.negativeDescriptiveName;
            var row = new Binding { Action = a, Range = range, Label = label };
            for (int c = 0; c < 3; c++)
            {
                row.Allowed[c] = !Skipped(c, name);
                if (!row.Allowed[c] || maps[c] == null) continue;
                foreach (var em in maps[c].ElementMapsWithAction(a.id))
                {
                    if (!em.ShowInField(range)) continue;
                    row.Keys[c] = em.elementIdentifierName;
                    row.MapIds[c] = em.id;
                    break;
                }
            }
            _rows.Add(row);
        }

        private static bool Skipped(int column, string name)
        {
            switch (column)
            {
                case 0: return name == "Move Horizontal" || name == "Move Vertical" || name == "Far Look" || name == "Player Menu" || name == "Effects Cycle";
                case 1: return name == "Move Horizontal" || name == "Move Vertical" || name == "Move Left" || name == "Move Right" || name == "Far Look" ||
                               name == "Player Menu" || name == "Effects Cycle" || name == "Move Up" || name == "Move Down";
                default: return name == "Move Left" || name == "Move Right" || name == "Move Up" || name == "Move Down" || name == "Run";
            }
        }

        private static void DrawControls(Flow f)
        {
            if (_rows == null) BuildRows();
            var c = f.Row(Ui.RowH, 0, 0);
            bool rel = GameSettings.GetInt("RelativeControls") == 1;
            if (Ui.Check(c[0], rel, "Relative controls") != rel)
            {
                SetSetting("RelativeControls", rel ? 0 : 1);
                if (Player.Instance != null) Player.Instance.relativeControls = !rel;
            }
            bool wheel = GameSettings.GetInt("MouseScrollHotbar") == 1;
            if (Ui.Check(c[1], wheel, "Mouse wheel scrolls the hotbar") != wheel) SetSetting("MouseScrollHotbar", wheel ? 0 : 1);
            c = f.Row(Ui.RowH, 0, 90, 110);
            bool noPad = GameSettings.GetInt("DisableControllerMode") == 1;
            if (Ui.Check(c[0], noPad, "Turn the controller off") != noPad)
            {
                SetSetting("DisableControllerMode", noPad ? 0 : 1);
                var pad = Menu != null ? Menu.ControlsMenu.gamepadMap : null;
                if (pad != null) pad.enabled = noPad; // on again when it was off
            }
            Ui.Label(c[1], "Aim assist");
            Choice(c[2], "aim", AimAssist, Mathf.Clamp(GameSettings.GetInt("AimAssist"), 0, 2), i =>
            {
                SetSetting("AimAssist", i);
                if (Menu != null) Menu.ControlsMenu.aimAssistMode = i;
            });
            f.Space(4);

            // The list fills the panel above the status line.
            float statusH = PixelFont.Line + 4 * S;
            var panel = new Rect(f.Area.x, f.Y, f.Width, Mathf.Max(60 * S, f.Area.height - (f.Y - f.Area.y) - statusH - f.Gap));
            GUI.Box(panel, GUIContent.none, MenuSkin.Sunken);
            var inner = new Rect(panel.x + 2 * S, panel.y + 2 * S, panel.width - 4 * S, panel.height - 4 * S);
            float headH = PixelFont.Line + 6 * S, scrollW = 10 * S, rowH = PixelFont.Line + 6 * S;
            float[] widths = { 0, 120, 120, 120 };
            var head = Ui.Split(new Rect(inner.x, inner.y, inner.width - scrollW, headH), widths);
            string[] heads = { "Action", Columns[0], Columns[1], Columns[2] };
            for (int i = 0; i < 4; i++)
            {
                GUI.Box(new Rect(head[i].x, head[i].y, head[i].width + 4 * S, head[i].height), GUIContent.none, MenuSkin.Button);
                Ui.Label(new Rect(head[i].x + 4 * S, head[i].y, head[i].width - 4 * S, head[i].height), heads[i]);
            }
            var listArea = new Rect(inner.x, inner.y + headH, inner.width, inner.height - headH);
            var view = new Rect(0, 0, listArea.width - scrollW, Mathf.Max(listArea.height, _rows.Count * rowH));
            _listScroll = GUI.BeginScrollView(listArea, _listScroll, view, false, _rows.Count * rowH > listArea.height, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (_rows.Count == 0) Ui.Label(new Rect(6 * S, 4 * S, view.width - 12 * S, rowH), "The game's controls aren't loaded yet.", MenuSkin.Dim);
            var rc = Ui.Split(new Rect(0, 0, view.width, rowH), widths);
            for (int i = 0; i < _rows.Count; i++)
            {
                var b = _rows[i];
                float y = i * rowH;
                bool mappingThis = _mapRow == b;
                if (mappingThis) GUI.Box(new Rect(0, y, view.width, rowH), GUIContent.none, MenuSkin.Selected);
                Ui.Label(new Rect(rc[0].x + 4 * S, y, rc[0].width - 4 * S, rowH), PixelFont.Fit(b.Label, rc[0].width - 6 * S), mappingThis ? MenuSkin.Bright : MenuSkin.Text);
                for (int col = 0; col < 3; col++)
                {
                    var cell = new Rect(rc[col + 1].x, y + S, rc[col + 1].width, rowH - 2 * S);
                    if (!b.Allowed[col]) continue;
                    if (col == 2 || Map(ColumnTypes[col]) == null)
                    {
                        // The game doesn't let you change the controller's buttons: shown only.
                        Ui.Label(new Rect(cell.x + 4 * S, cell.y, cell.width - 4 * S, cell.height), PixelFont.Fit(b.Keys[col] ?? "-", cell.width - 6 * S), MenuSkin.Dim);
                        continue;
                    }
                    bool active = mappingThis && _mapCol == col;
                    string text = active ? (col == 0 ? "Press a key" : "Press a button") : b.Keys[col] ?? "-";
                    if (Ui.Button(cell, PixelFont.Fit(text, cell.width - 6 * S)) && _mapRow == null) BeginMapping(b, col);
                }
            }
            GUI.EndScrollView();
            f.Y = panel.yMax + f.Gap;
            string status = _status;
            if (_mapRow != null)
            {
                float left = _mapper != null && _mapper.status == RW.InputMapper.Status.Listening ? _mapper.timeRemaining : 0f;
                status = $"{(_mapCol == 0 ? "Press the new key" : "Click the new mouse button")} for {_mapRow.Label}{(left > 0 ? $" ({Mathf.CeilToInt(left)} s)" : "")}. Escape cancels.";
            }
            if (status == null) status = "Click a key or mouse button to change it. Controller buttons can't be changed.";
            Ui.Label(new Rect(f.Area.x + 2 * S, f.Y, f.Width, statusH), PixelFont.Fit(status, f.Width - 4 * S), _statusWarn && _mapRow == null ? MenuSkin.Warn : MenuSkin.Dim);
            f.Y += statusH;
        }

        /// <summary>Start listening for a new key (once the mouse button that chose it is let go, so the click isn't taken).</summary>
        private static void BeginMapping(Binding b, int col)
        {
            _mapRow = b; _mapCol = col; _startWhenReleased = true;
            _status = null; _statusWarn = false;
            SetAssigning(true);
        }

        /// <summary>
        /// The game's ControlsMenu.assigningMapping: while it's set the game's Escape and controller switch leave the
        /// keys alone. It's let go a few frames after the mapping ends, as the game does.
        /// </summary>
        private static void SetAssigning(bool on)
        {
            var cm = Menu != null ? Menu.ControlsMenu : null;
            if (cm == null) return;
            if (on) cm.assigningMapping = true;
            else if (Singleton<Controller>.Instance != null) Singleton<Controller>.Instance.waitFramesAndRun(() => { if (_mapRow == null) cm.assigningMapping = false; }, 5);
            else cm.assigningMapping = false;
        }

        private static void EnsureMapper()
        {
            if (_mapper != null) return;
            _mapper = new RW.InputMapper();
            var o = _mapper.options;
            // As ControlsMenu.OnEnableIM sets them.
            o.ignoreMouseXAxis = true;
            o.ignoreMouseYAxis = true;
            o.allowKeyboardModifierKeyAsPrimary = true;
            o.allowKeyboardKeysWithModifiers = false;
            // Escape cancels; it's never taken as a key.
            o.isElementAllowedCallback = info => !(info.controllerType == RW.ControllerType.Keyboard && info.keyboardKey == KeyCode.Escape);
            _mapper.InputMappedEvent += OnMapped;
            _mapper.ConflictFoundEvent += d => _conflict = d;
            _mapper.TimedOutEvent += _ => { Plugin.Log.LogInfo("[options] the mapper timed out"); StopMapping("No key was pressed: nothing changed.", fromMapper: true); };
            _mapper.CanceledEvent += d => { Plugin.Log.LogInfo($"[options] the mapper cancelled: {d.message}"); StopMapping(null, fromMapper: true); };
            _mapper.ErrorEvent += d => { Plugin.Log.LogInfo($"[options] the mapper failed: {d.message}"); StopMapping($"Couldn't change it: {d.message}", warn: true, fromMapper: true); };
            _mapper.StoppedEvent += _ => StopMapping(null, fromMapper: true);
        }

        private static void UpdateMapping()
        {
            if (_mapRow == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { _escTaken = Time.frameCount; StopMapping("Cancelled: nothing changed."); return; }
            if (_startWhenReleased && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                _startWhenReleased = false;
                EnsureMapper();
                var map = Map(ColumnTypes[_mapCol]);
                if (map == null) { StopMapping("That controller isn't there.", warn: true); return; }
                bool started = _mapper.Start(new RW.InputMapper.Context
                {
                    actionId = _mapRow.Action.id,
                    controllerMap = map,
                    actionRange = _mapRow.Range,
                    actionElementMapToReplace = _mapRow.MapIds[_mapCol] >= 0 ? map.GetElementMap(_mapRow.MapIds[_mapCol]) : null,
                });
                Plugin.Log.LogInfo($"[options] listening for {Columns[_mapCol].ToLowerInvariant()} input for {_mapRow.Label}: {(started ? "started" : "didn't start")}");
                if (!started) StopMapping("Couldn't listen for a key.", warn: true);
            }
            // A key another action has: taken from it, unless that one can't be changed (ControlsMenu.Update).
            if (_conflict != null && _mapper != null && _mapper.status == RW.InputMapper.Status.AwaitingResponse)
            {
                var data = _conflict;
                _conflict = null;
                string other = "", key = "";
                bool fixedOne = false;
                foreach (var c in data.conflicts)
                {
                    if (!c.isUserAssignable) fixedOne = true;
                    if (other == "") { other = c.action != null ? c.action.descriptiveName : "another action"; key = c.elementDisplayName; }
                }
                if (fixedOne)
                {
                    data.responseCallback(RW.InputMapper.ConflictResponse.Cancel);
                    StopMapping($"{key} is the game's own for {other}: nothing changed.", warn: true);
                }
                else
                {
                    _status = $"{key} was taken from {other}.";
                    _statusWarn = true;
                    data.responseCallback(RW.InputMapper.ConflictResponse.Replace);
                }
            }
        }

        private static void OnMapped(RW.InputMapper.InputMappedEventData d)
        {
            string label = _mapRow != null ? _mapRow.Label : "";
            string key = d.actionElementMap != null ? d.actionElementMap.elementIdentifierName : "?";
            if (_status == null || !_statusWarn) { _status = $"{label}: {key}."; _statusWarn = false; }
            else _status = $"{label}: {key}. " + _status;
            Plugin.Log.LogInfo($"[options] {label} is now {key}");
            SaveControls();
            _mapRow = null;
            SetAssigning(false);
        }

        private static bool _stopping;

        /// <summary>
        /// End the mapping. From one of the mapper's own events (<paramref name="fromMapper"/>) it has already
        /// stopped, so it isn't told to again: Stop raises events itself, and calling it from them recursed until the
        /// stack ran out, which took the whole game down.
        /// </summary>
        private static void StopMapping(string status, bool warn = false, bool fromMapper = false)
        {
            if (_stopping) return;
            _stopping = true;
            try
            {
                bool was = _mapRow != null;
                _mapRow = null;
                _startWhenReleased = false;
                _conflict = null;
                if (status != null) { _status = status; _statusWarn = warn; }
                if (!fromMapper && _mapper != null && _mapper.status != RW.InputMapper.Status.Idle) _mapper.Stop();
                if (was) SetAssigning(false);
            }
            finally { _stopping = false; }
        }

        /// <summary>The key maps into the game's store (ControlsMenu.Save), and the game's own copies of the maps refreshed.</summary>
        internal static void SaveControls()
        {
            RW.ReInput.userDataStore?.Save();
            if (Menu != null && Menu.ControlsMenu != null) Menu.ControlsMenu.loadMaps();
            _rows = null;
        }

        /// <summary>The game's "revert controls": its default key maps, and the switches as a first start sets them.</summary>
        private static void ResetControls()
        {
            StopMapping(null);
            var cm = Menu != null ? Menu.ControlsMenu : null;
            if (cm != null) cm.revertControls();
            else
            {
                var p = Singleton<InputScript>.Instance.rewiredPlayer;
                p.controllers.maps.LoadDefaultMaps(RW.ControllerType.Joystick);
                p.controllers.maps.LoadDefaultMaps(RW.ControllerType.Keyboard);
                p.controllers.maps.LoadDefaultMaps(RW.ControllerType.Mouse);
                RW.ReInput.userDataStore?.Save();
                GameSettings.SetInt("RelativeControls", 0);
                GameSettings.SetInt("MouseScrollHotbar", 1);
                GameSettings.SetInt("DisableControllerMode", 0);
            }
            if (Player.Instance != null) Player.Instance.relativeControls = false;
            GameSettings.Save();
            _rows = null;
            _status = "The keys are back as the game has them.";
            _statusWarn = false;
            Plugin.Log.LogInfo("[options] controls reset to the defaults");
        }

        // ---- for the tests -------------------------------------------------------------------------

        internal static void TestTab(int t) { SwitchTab((Tab)t); }
        internal static string[] TestTabNames => TabNames;
        /// <summary>Open a dropdown as a click on it does ("res", "lang", ...), the next time it's drawn.</summary>
        internal static void TestOpenDrop(string key) { _testDropKey = key; }
        private static string _testDropKey;
        internal static bool TestMapping => _mapRow != null;
        internal static string TestMapperStatus => _mapper == null ? "none" : _mapper.status.ToString();

        /// <summary>The keyboard rows (label, key) as the window lists them.</summary>
        internal static List<(string label, string key, int actionId, int range)> TestRows()
        {
            BuildRows();
            var l = new List<(string, string, int, int)>();
            foreach (var b in _rows) if (b.Allowed[0]) l.Add((b.Label, b.Keys[0], b.Action.id, (int)b.Range));
            return l;
        }

        /// <summary>Start listening for a key for the keyboard row with this label, as a click on it does.</summary>
        internal static bool TestBeginMapping(string label)
        {
            if (_rows == null) BuildRows();
            var b = _rows.Find(r => r.Label == label && r.Allowed[0]);
            if (b == null) return false;
            BeginMapping(b, 0);
            return true;
        }

        internal static void TestEscape() { _escTaken = Time.frameCount; StopMapping("Cancelled: nothing changed."); }

        /// <summary>
        /// Put <paramref name="key"/> on the keyboard row with this label as a finished mapping does (the element map
        /// replaced in the player's keyboard map, then saved), for a test that can't press a real key.
        /// </summary>
        internal static bool TestAssign(string label, KeyCode key)
        {
            if (_rows == null) BuildRows();
            var b = _rows.Find(r => r.Label == label && r.Allowed[0]);
            var map = Map(RW.ControllerType.Keyboard);
            if (b == null || map == null) return false;
            var ident = RW.ReInput.controllers.Keyboard.GetElementIdentifierByKeyCode(key);
            if (ident == null) return false;
            bool ok = map.ReplaceOrCreateElementMap(new RW.ElementAssignment(RW.ControllerType.Keyboard, RW.ControllerElementType.Button, ident.id, b.Range,
                key, RW.ModifierKeyFlags.None, b.Action.id, b.Range == RW.AxisRange.Negative ? RW.Pole.Negative : RW.Pole.Positive, false, b.MapIds[0]));
            SaveControls();
            return ok;
        }

        /// <summary>The key on the keyboard row with this label (None: none).</summary>
        internal static KeyCode TestKeyOf(string label)
        {
            BuildRows();
            var b = _rows.Find(r => r.Label == label && r.Allowed[0]);
            var map = Map(RW.ControllerType.Keyboard);
            var em = b != null && map != null && b.MapIds[0] >= 0 ? map.GetElementMap(b.MapIds[0]) : null;
            return em != null ? em.keyCode : KeyCode.None;
        }

        /// <summary>Some action in the keyboard map has this key.</summary>
        internal static bool TestKeyUsed(KeyCode key)
        {
            var map = Map(RW.ControllerType.Keyboard);
            if (map == null) return true;
            foreach (var em in map.AllMaps) if (em.keyCode == key) return true;
            return false;
        }

        /// <summary>The game's stored key maps read back into the player's maps (ControlsMenu.Load).</summary>
        internal static void TestReloadControls()
        {
            RW.ReInput.userDataStore?.Load();
            if (Menu != null && Menu.ControlsMenu != null) Menu.ControlsMenu.loadMaps();
            _rows = null;
        }
    }
}
