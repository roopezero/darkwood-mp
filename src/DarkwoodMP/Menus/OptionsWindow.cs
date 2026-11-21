using System;
using System.Collections.Generic;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The Options window, in place of the game's options screen
    /// (MenuWindows.OpenOptions: "Options" in the main menu and the pause
    /// menu). Tabs as the server browser's: Video, Audio, Game, Controls,
    /// Multiplayer. Every setting the game's screen had is here, read from and
    /// written to the game's own store (GameSettings, which is Unity's
    /// PlayerPrefs; Rewired's key maps through ReInput.userDataStore, also
    /// PlayerPrefs), and applied with the game's own calls, as its screen did:
    /// - Resolution and full screen change at once and ask "Keep these
    ///   settings?", going back after 15 seconds without an answer (the game's
    ///   resolution box, with a countdown).
    /// - Brightness changes at once (VideoMenu.setGamma), as in the game's
    ///   brightness box.
    /// - Post-processing, light quality, V-Sync, camera zoom, language and the
    ///   two volumes wait for Apply (the volumes are heard at once, as the
    ///   game's sliders did); Revert, or closing and choosing Discard, puts
    ///   them back. A new language needs Darkwood started again, so applying
    ///   one asks to quit, as the game's restart box.
    /// - Controls: the game's switches change at once. Keys and mouse buttons
    ///   are changed by clicking one and pressing the new one (Rewired's
    ///   InputMapper, set up as the game's ControlsMenu: a key that another
    ///   action has is taken from it, unless that one can't be changed; Escape
    ///   cancels). Controller buttons are shown; the game doesn't let you
    ///   change them. "Reset to defaults" is the game's revertControls.
    /// - Multiplayer: your name (the mod's General.PlayerName) and the hosting
    ///   settings from the mod's config.
    /// </summary>
    internal sealed partial class OptionsWindow
    {
        private enum Tab
        {
            Video,
            Audio,
            Game,
            Controls,
            Multiplayer
        }

        private static readonly string[] TabNames =
            { "Video", "Audio", "Game", "Controls", "Multiplayer" };

        private UiWindow _win;
        private Tab _tab;

        // What the tabs show for the settings that wait for Apply; the game's
        // stored values when nothing is changed.
        private int _postFx, _light, _vsync, _zoom;
        private string _lang;
        private float _music, _sound;
        private bool _gammaChanged;

        private static readonly string[] LangCodes =
        {
            "EN",
            "PL",
            "RU",
            "ES",
            "DE",
            "PT_BR",
            "IT",
            "TR",
            "ZH",
            "HU",
            "FR"
        };

        private static readonly string[] LangNames =
        {
            "English",
            "Polish",
            "Russian",
            "Spanish",
            "German",
            "Portuguese (Brazil)",
            "Italian",
            "Turkish",
            "Chinese",
            "Hungarian",
            "French"
        };

        // PostFXQuality: 0 high, 1 low
        private static readonly string[] HighLow = { "High", "Low" };
        // LightQuality: 0 low, 1 high
        private static readonly string[] LowHigh = { "Low", "High" };
        private static readonly string[] AimAssist = { "Off", "Low", "High" };
        private static readonly string[] NewChars = { "fresh", "copy" };
        private static readonly string[] NewCharNames =
            { "Nothing, beside you", "A copy of your character" };

        private const float KeepSeconds = 15f;

        public bool IsOpen => _win != null && _win.Open;

        private ConfirmDialog Confirm => Plugin.Instance.Host.Confirm;

        public OptionsWindow()
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

        public void Open()
        {
            Load();
            _rows = null;
            _status = null;
            _drop = null;
            _win.ResetPlace();
            _win.Show();
            Plugin.Log.LogInfo("[options] opened");
        }

        /// <summary>
        /// Close as Close does: asks first when something is changed and not
        /// applied.
        /// </summary>
        public void RequestClose()
        {
            if (!IsOpen)
            {
                return;
            }

            if (Pending)
            {
                Confirm.Ask("Options", "Apply the changes you made?",
                    "Apply", () =>
                {
                    if (Apply())
                    {
                        CloseNow();
                    }
                }, "Discard", () =>
                {
                    Revert();
                    CloseNow();
                });
            }
            else
            {
                CloseNow();
            }
        }

        private void CloseNow()
        {
            _win.OnClose = null;
            _win.Close();
            _win.OnClose = OnCloseBox;
            Closed();
        }

        private void OnCloseBox()
        {
            // The close box has already closed it: open again to ask, if
            // there's something to ask about.
            if (Pending)
            {
                _win.Show();
                RequestClose();
            }
            else
            {
                Closed();
            }
        }

        private void Closed()
        {
            StopMapping(null);
            _drop = null;
            if (_gammaChanged)
            {
                _gammaChanged = false;
                GameSettings.Save();
            }

            Plugin.Log.LogInfo("[options] closed");
        }

        // ---- the game's settings --------------------------------------------

        private MainMenu Menu => Singleton<MainMenu>.Instance;

        private void Load()
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
        public bool Pending =>
            _postFx != GameSettings.GetInt("PostFXQuality")
                || _light != GameSettings.GetInt("LightQuality") ||
            _vsync != GameSettings.GetInt("Vsync")
                || _zoom != GameSettings.GetInt("CameraZoom") ||
            _lang != GameSettings.GetString("LanguageCode") ||
            !Mathf.Approximately(_music, GameSettings.GetFloat("MusicVolume"))
                || !Mathf.Approximately(
                    _sound, GameSettings.GetFloat("SoundVolume"));

        /// <summary>
        /// Apply what waits, as MainMenu.applyOptionsSettings does. A new
        /// language asks to quit first (false: not applied yet, the answer does
        /// it).
        /// </summary>
        public bool Apply()
        {
            if (_lang != GameSettings.GetString("LanguageCode"))
            {
                string lost = "";
                if (Singleton<WorldGenerator>.Instance != null)
                {
                    lost = " What you did since you last saved is lost.";
                }

                Confirm.Ask("Language",
                    "The new language is used from when Darkwood starts again. "
                        + "Apply the changes and quit now?" + lost,
                    "Quit", () =>
                    {
                        ApplyNow();
                        Core.quitGame();
                    }, "Not now",
                    () =>
                    {
                        _lang = GameSettings.GetString("LanguageCode");
                    });

                return false;
            }

            ApplyNow();
            return true;
        }

        private void ApplyNow()
        {
            VideoMenu vm = null;
            if (Menu != null)
            {
                vm = Menu.VideoMenu;
            }

            var cam = Singleton<CamMain>.Instance;
            if (_postFx != GameSettings.GetInt("PostFXQuality"))
            {
                GameSettings.SetInt("PostFXQuality", _postFx);
                if (vm != null)
                {
                    vm.postFXQuality = (VideoMenu.PostFXQuality)_postFx;
                    vm.setPostFXQuality();
                }

                if (cam != null)
                {
                    cam.setRenderTargets();
                }
            }

            if (_light != GameSettings.GetInt("LightQuality"))
            {
                GameSettings.SetInt("LightQuality", _light);
                if (vm != null)
                {
                    vm.destLightQuality = _light;
                }

                if (cam != null)
                {
                    cam.setRenderTargets();
                }
            }

            if (_vsync != GameSettings.GetInt("Vsync"))
            {
                GameSettings.SetInt("Vsync", _vsync);
                if (vm != null)
                {
                    vm.destVsync = _vsync;
                }

                QualitySettings.vSyncCount = _vsync;
            }

            if (_zoom != GameSettings.GetInt("CameraZoom"))
            {
                GameSettings.SetInt("CameraZoom", _zoom);
                if (vm != null)
                {
                    vm.destCameraZoom = _zoom;
                }

                Controller instance = Singleton<Controller>.Instance;
                if (instance != null)
                {
                    instance.refreshZoom();
                }
            }

            if (!Mathf.Approximately(
                _music, GameSettings.GetFloat("MusicVolume")))
            {
                GameSettings.SetFloat("MusicVolume", _music);
            }

            if (!Mathf.Approximately(
                _sound, GameSettings.GetFloat("SoundVolume")))
            {
                GameSettings.SetFloat("SoundVolume", _sound);
            }

            if (!string.IsNullOrEmpty(_lang)
                && _lang != GameSettings.GetString("LanguageCode"))
            {
                GameSettings.SetString("LanguageCode", _lang);
                if (vm != null)
                {
                    vm.destLanguageCode = _lang;
                }
            }

            _gammaChanged = false;
            GameSettings.Save();
            Plugin.Log.LogInfo(
                $"[options] applied: post-fx {_postFx}, lights {_light}, vsync "
                    + $"{_vsync}, zoom {_zoom}, music {_music:0.00}, sound "
                    + $"{_sound:0.00}, language {_lang}");
        }

        /// <summary>
        /// Back to the stored values (the volumes heard as they were), as
        /// UI.revertOptionsSettings.
        /// </summary>
        public void Revert()
        {
            Load();
            SetVolume(music: true, _music);
            SetVolume(music: false, _sound);
        }

        /// <summary>
        /// A volume, heard at once (the game's slider, which sets the mixer);
        /// stored on Apply.
        /// </summary>
        internal void SetVolume(bool music, float v)
        {
            AudioMenu am = null;
            if (Menu != null)
            {
                am = Menu.AudioMenu;
            }

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

            if (slider != null)
            {
                v = Mathf.Clamp(v, slider.minValue, slider.maxValue);
            }

            if (music)
            {
                _music = v;
            }
            else
            {
                _sound = v;
            }

            if (slider != null)
            {
                slider.Magnitude = v;
            }
            else if (am != null)
            {
                if (music)
                {
                    am.setMusicVolume(v);
                }
                else
                {
                    am.setSoundVolume(v);
                }
            }
        }

        private void SetGamma(float v)
        {
            OptionsSlider g = null;
            if (Menu != null)
            {
                g = Menu.gammaSlider;
            }

            if (g != null)
            {
                // VideoMenu.setGamma: the picture and GameSettings' "Gamma"
                g.Magnitude = v;
            }
            else if (Menu != null)
            {
                Menu.VideoMenu.setGamma(v);
            }

            _gammaChanged = true;
        }

        /// <summary>
        /// A new resolution or full screen, at once; kept only if the player
        /// says so within 15 seconds (MainMenu.confirmBoxYes/No).
        /// </summary>
        private void ChangeScreen(int w, int h, bool full)
        {
            int ow = GameSettings.GetInt("ScreenWidth"),
                oh = GameSettings.GetInt("ScreenHeight");
            bool ofull = GameSettings.GetInt("Fullscreen") == 1;
            if (ow <= 0 || oh <= 0)
            {
                ow = Screen.width;
                oh = Screen.height;
            }

            FullScreenMode fullscreenMode;
            if (full)
            {
                fullscreenMode = FullScreenMode.FullScreenWindow;
            }
            else
            {
                fullscreenMode = FullScreenMode.Windowed;
            }

            Screen.SetResolution(w, h, fullscreenMode);
            string text = "window";
            if (full)
            {
                text = "full screen";
            }

            Plugin.Log.LogInfo(
                $"[options] screen {w}x{h} {text}, asking to keep it");
            string mode = "in a window";
            if (full)
            {
                mode = "full screen";
            }

            AskKeep($"The screen is now {w}x{h}, {mode}.", () =>
            {
                GameSettings.SetInt("ScreenWidth", w);
                GameSettings.SetInt("ScreenHeight", h);
                int fullFlag = 0;
                if (full)
                {
                    fullFlag = 1;
                }

                GameSettings.SetInt("Fullscreen", fullFlag);
                if (Menu != null)
                {
                    if (full)
                    {
                        Menu.VideoMenu.destFulscreen = 1;
                    }
                    else
                    {
                        Menu.VideoMenu.destFulscreen = 0;
                    }
                }

                GameSettings.Save();
                Plugin.Log.LogInfo("[options] screen kept");
            }, () =>
            {
                FullScreenMode oldMode;
                if (ofull)
                {
                    oldMode = FullScreenMode.FullScreenWindow;
                }
                else
                {
                    oldMode = FullScreenMode.Windowed;
                }

                Screen.SetResolution(ow, oh, oldMode);
                Plugin.Log.LogInfo($"[options] screen back to {ow}x{oh}");
            });
        }

        internal void AskKeep(string text, Action keep, Action back) =>
            Confirm.Ask(
                "Keep these settings?", text, "Keep", keep, "Go back", back,
                timeout: KeepSeconds);

        private void SetSetting(string key, int v)
        {
            GameSettings.SetInt(key, v);
            GameSettings.Save();
            Plugin.Log.LogInfo($"[options] {key} = {v}");
        }
    }
}
