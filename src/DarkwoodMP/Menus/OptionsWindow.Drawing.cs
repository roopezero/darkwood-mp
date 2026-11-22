using System;
using System.Collections.Generic;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.Menus
{
    internal sealed partial class OptionsWindow
    {
        // ---- drawing --------------------------------------------------------

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            var e = Event.current;
            // A click outside the window closes an open list (the window
            // doesn't get it).
            if (_drop != null && e.type == EventType.MouseDown
                && !_win.Rect.Contains(e.mousePosition))
            {
                _drop = null;
            }

            _win.OnGUI();
        }

        /// <summary>
        /// Every frame (Plugin.Update): Escape, and the key mapping's steps
        /// that wait for a frame.
        /// </summary>
        public void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            UpdateMapping();
            if (Input.GetKeyDown(KeyCode.Escape) && !Confirm.Open
                && Time.frameCount != _escTaken)
            {
                if (_drop != null)
                {
                    _drop = null;
                }
                else
                {
                    RequestClose();
                }
            }
        }

        private float S => PixelFont.Scale;
        private const float LabelW = 150;

        private void DrawBody(Flow f)
        {
            var tabs = Ui.Split(
                new Rect(
                    f.Area.x, f.Y,
                    92 * TabNames.Length * S + 4 * S * (TabNames.Length - 1),
                    Ui.RowH),
                92, 92, 92, 92, 92);
            for (int i = 0; i < TabNames.Length; i++)
            {
                if (Ui.Tab(tabs[i], TabNames[i], (int)_tab == i))
                {
                    SwitchTab((Tab)i);
                }
            }

            f.Y += Ui.RowH;
            var panel = new Rect(
                f.Area.x, f.Y, f.Width,
                Mathf.Max(80 * S, f.Area.height - (f.Y - f.Area.y)));
            GUI.Box(panel, GUIContent.none, MenuSkin.Sunken);
            var inner = new Flow(
                new Rect(
                    panel.x + 10 * S, panel.y + 8 * S, panel.width - 20 * S,
                    panel.height - 16 * S));
            switch (_tab)
            {
                case Tab.Video:
                    DrawVideo(inner);
                    break;
                case Tab.Audio:
                    DrawAudio(inner);
                    break;
                case Tab.Game:
                    DrawGame(inner);
                    break;
                case Tab.Controls:
                    DrawControls(inner);
                    break;
                case Tab.Multiplayer:
                    DrawMultiplayer(inner);
                    break;
            }

            f.Y = panel.yMax;
        }

        private void SwitchTab(Tab t)
        {
            if (t == _tab)
            {
                return;
            }

            StopMapping(null);
            _drop = null;
            _tab = t;
        }

        private Rect[] Setting(Flow f, string label)
        {
            var c = f.Row(Ui.RowH, LabelW, 0);
            Ui.Label(c[0], label);
            return c;
        }

        private void DrawVideo(Flow f)
        {
            var res = Core.getResolutions();
            var names = new List<string>();
            int cur = -1;
            for (int i = 0; i < res.Count; i++)
            {
                names.Add($"{res[i].width}x{res[i].height}");
                if (res[i].width == Screen.width
                    && res[i].height == Screen.height)
                {
                    cur = i;
                }
            }

            if (cur < 0)
            {
                names.Add($"{Screen.width}x{Screen.height}");
                cur = names.Count - 1;
            }

            var c = Setting(f, "Resolution");
            Choice(Ui.Split(c[1], 160, 0)[0], "res", names.ToArray(), cur, i =>
            {
                if (i < res.Count
                    && (res[i].width != Screen.width
                        || res[i].height != Screen.height))
                {
                    ChangeScreen(
                        res[i].width, res[i].height, Screen.fullScreen);
                }
            });

            c = Setting(f, "Full screen");
            bool full = Screen.fullScreen;
            if (Ui.Check(Ui.Split(c[1], 24, 0)[0], full, "") != full)
            {
                ChangeScreen(Screen.width, Screen.height, !full);
            }

            c = Setting(f, "Brightness");
            OptionsSlider g = null;
            if (Menu != null)
            {
                g = Menu.gammaSlider;
            }

            float gmin = 0.2f;
            if (g != null)
            {
                gmin = g.minValue;
            }

            float gmax = 1.2f;
            if (g != null)
            {
                gmax = g.maxValue;
            }

            float gamma = GameSettings.GetFloat("Gamma");
            var gc = Ui.Split(c[1], 0, 44);
            float ng = Slider(gc[0], gamma, gmin, gmax);
            if (!Mathf.Approximately(ng, gamma))
            {
                SetGamma(ng);
            }

            Ui.Label(
                gc[1], Percent(gamma, gmin, gmax), MenuSkin.Bright,
                TextAnchor.MiddleRight);

            f.Space(6);
            c = Setting(f, "Post-processing");
            Choice(
                Ui.Split(c[1], 160, 0)[0], "postfx", HighLow, _postFx,
                i => _postFx = i);
            c = Setting(f, "Light quality");
            Choice(
                Ui.Split(c[1], 160, 0)[0], "light", LowHigh, _light,
                i => _light = i);
            c = Setting(f, "V-Sync");
            if (Ui.Check(Ui.Split(c[1], 24, 0)[0], _vsync == 1, ""))
            {
                _vsync = 1;
            }
            else
            {
                _vsync = 0;
            }

            f.Space(6);
            Ui.Text(
                f,
                "Resolution, full screen and brightness change at once. The "
                    + "others wait for Apply.",
                MenuSkin.Dim);
        }

        private void DrawAudio(Flow f)
        {
            AudioMenu am = null;
            if (Menu != null)
            {
                am = Menu.AudioMenu;
            }

            for (int k = 0; k < 2; k++)
            {
                bool music = k == 0;
                OptionsSlider slider;
                if (am == null)
                {
                    slider = null;
                }
                else if (music)
                {
                    slider = am.musicSlider;
                }
                else
                {
                    slider = am.soundSlider;
                }

                float min = 0f;
                if (slider != null)
                {
                    min = slider.minValue;
                }

                float max = 1f;
                if (slider != null)
                {
                    max = slider.maxValue;
                }

                float v;
                if (music)
                {
                    v = _music;
                }
                else
                {
                    v = _sound;
                }

                string label = "Sound volume";
                if (music)
                {
                    label = "Music volume";
                }

                var c = Setting(f, label);
                var cc = Ui.Split(c[1], 0, 44);
                float nv = Slider(cc[0], v, min, max);
                if (!Mathf.Approximately(nv, v))
                {
                    SetVolume(music, nv);
                }

                float volume;
                if (music)
                {
                    volume = _music;
                }
                else
                {
                    volume = _sound;
                }

                Ui.Label(
                    cc[1], Percent(volume, min, max), MenuSkin.Bright,
                    TextAnchor.MiddleRight);
            }

            f.Space(6);
            Ui.Text(
                f, "You hear a new volume at once. Apply keeps it.",
                MenuSkin.Dim);
        }

        private void DrawGame(Flow f)
        {
            var c = Setting(f, "Language");
            int li = Array.IndexOf(LangCodes, _lang);
            Choice(
                Ui.Split(c[1], 200, 0)[0], "lang", LangNames, li,
                i => _lang = LangCodes[i]);
            c = Setting(f, "Camera zoom");
            if (Ui.Check(
                c[1], _zoom == 1,
                "Closer view (always on above 1920 pixels wide)"))
            {
                _zoom = 1;
            }
            else
            {
                _zoom = 0;
            }

            f.Space(6);
            var cfg = Plugin.Cfg;
            c = Setting(f, "Studio logo");
            cfg.SkipIntro.Value = Ui.Check(
                c[1], cfg.SkipIntro.Value,
                "Skip it at start (about 6 seconds sooner)");
            f.Space(6);
            Ui.Text(
                f,
                "The language and camera zoom wait for Apply; a new language "
                    + "is used after a restart.",
                MenuSkin.Dim);
        }

        private void DrawMultiplayer(Flow f)
        {
            var cfg = Plugin.Cfg;
            Net.Session session = null;
            if (Plugin.Instance != null)
            {
                session = Plugin.Instance.Host.Session;
            }

            bool inGame = session != null && session.Active;
            var c = Setting(f, "Your name");
            if (inGame)
            {
                Ui.Label(c[1], session.LocalName, MenuSkin.Bright);
            }
            else
            {
                string n = Ui.Field(c[1], cfg.PlayerName.Value, 32);
                if (n != cfg.PlayerName.Value)
                {
                    Plugin.Instance.SetPlayerName(n);
                }
            }

            string shown = "";
            if (session != null)
            {
                shown = session.LocalName;
            }

            string text = NameRules.OthersSee(shown);
            Verdict rename = NameRules.MayRename(inGame);
            if (rename.Refused)
            {
                text = Refusals.Text(rename);
            }

            Ui.Text(f, text, MenuSkin.Dim);

            Ui.Heading(f, "Hosting");
            c = Setting(f, "Server name");
            cfg.ServerName.Value = Ui.Field(c[1], cfg.ServerName.Value, 48);
            c = Setting(f, "Port");
            string port = Ui.Field(
                Ui.Split(c[1], 80, 0)[0], cfg.DirectPort.Value.ToString(), 5);
            cfg.DirectPort.Value =
                HostingRules.TypedPort(port, cfg.DirectPort.Value);

            c = Setting(f, "");
            cfg.OpenPort.Value = Ui.Check(
                c[1], cfg.OpenPort.Value,
                "Open it on my router (UPnP, NAT-PMP)");
            c = Setting(f, "Password");
            cfg.Password.Value =
                Ui.Field(c[1], cfg.Password.Value, 32, password: true);
            c = Setting(f, "Max players");
            var m = Ui.Split(c[1], 24, 30, 24, 0);
            bool fewer = HostingRules.MayRemovePlayer(cfg.MaxPlayers.Value);
            if (Ui.Button(m[0], "-", fewer))
            {
                cfg.MaxPlayers.Value--;
            }

            Ui.Label(
                m[1], cfg.MaxPlayers.Value.ToString(), MenuSkin.Bright,
                TextAnchor.MiddleCenter);
            bool more = HostingRules.MayAddPlayer(cfg.MaxPlayers.Value);
            if (Ui.Button(m[2], "+", more))
            {
                cfg.MaxPlayers.Value++;
            }

            c = Setting(f, "New players get");
            Choice(
                Ui.Split(c[1], 220, 0)[0], "newchars", NewCharNames,
                Array.IndexOf(NewChars, cfg.NewCharacters.Value),
                i => cfg.NewCharacters.Value = NewChars[i]);
            c = Setting(f, "Stack size");
            var sizes = HostingRules.StackSizes(cfg.StackSize.Value);

            var sizeNames = sizes.ConvertAll(n =>
            {
                if (n == 0)
                {
                    return "The game's own (2 to 50)";
                }

                return $"{n} (bigger ones keep theirs)";
            }).ToArray();

            Choice(
                Ui.Split(c[1], 220, 0)[0], "stacksize", sizeNames,
                sizes.IndexOf(cfg.StackSize.Value),
                i => cfg.StackSize.Value = sizes[i]);
            c = Setting(f, "World to join");
            cfg.LiveWorld.Value = Ui.Check(
                c[1], cfg.LiveWorld.Value,
                "As it is now (off: as you last saved it)");
            c = Setting(f, "");
            cfg.SnapshotOffThread.Value = Ui.Check(
                c[1], cfg.SnapshotOffThread.Value,
                "Write it for them in the background");
            Ui.Text(
                f,
                "These are used the next time you host (the stack size at "
                    + "once).",
                MenuSkin.Dim);
        }

        private void DrawFooter(Rect r)
        {
            var b = Ui.Split(r, 130, 0, 80, 80, 80);
            if (_tab == Tab.Controls && Ui.Button(b[0], "Reset to defaults"))
            {
                Confirm.Ask(
                    "Reset controls",
                    "Put every key and mouse button back as the game has them, "
                        + "and the switches above to theirs?",
                    "Reset", ResetControls);
            }

            bool pending = Pending;
            if (Ui.Button(b[2], "Apply", pending))
            {
                Apply();
            }

            if (Ui.Button(b[3], "Revert", pending))
            {
                Revert();
            }

            if (Ui.Button(b[4], "Close"))
            {
                RequestClose();
            }
        }

        private string Percent(float v, float min, float max) =>
            $"{Mathf.RoundToInt(Mathf.InverseLerp(min, max, v) * 100)}%";

        // ---- a slider -------------------------------------------------------

        private float Slider(Rect r, float v, float min, float max)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            float thumbW = 8 * S;
            float From(Vector2 m) => Mathf.Lerp(
                min, max,
                Mathf.Clamp01(
                    (m.x - r.x - thumbW / 2) / Mathf.Max(
                        1f, r.width - thumbW)));
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        v = From(e.mousePosition);
                        e.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        v = From(e.mousePosition);
                        e.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }

                    break;
                case EventType.Repaint:
                    // Held: follow the mouse itself, since slow movements send
                    // no MouseDrag (see UiWindow.MouseNow).
                    if (GUIUtility.hotControl == id)
                    {
                        v = From(
                            GUIUtility.ScreenToGUIPoint(
                                new Vector2(
                                    Input.mousePosition.x,
                                    Screen.height - Input.mousePosition.y)));
                    }

                    float t = Mathf.InverseLerp(min, max, v);
                    var track =
                        new Rect(r.x, r.center.y - 3 * S, r.width, 6 * S);
                    GUI.Box(track, GUIContent.none, MenuSkin.Sunken);
                    var fill = new Rect(
                        track.x + 2 * S, track.y + 2 * S,
                        Mathf.Max(0, (track.width - 4 * S) * t),
                        track.height - 4 * S);
                    GUI.Box(fill, GUIContent.none, MenuSkin.Selected);
                    var thumb = new Rect(
                        r.x + t * (r.width - thumbW), r.y + 3 * S, thumbW,
                        r.height - 6 * S);
                    GUI.Box(thumb, GUIContent.none, MenuSkin.Button);
                    break;
            }

            return v;
        }
    }
}
