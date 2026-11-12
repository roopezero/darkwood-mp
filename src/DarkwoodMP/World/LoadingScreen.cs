using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The game's own loading screen (UI.loadingScreen: its art, its bar UI.loadingBar and its percentage
    /// UI.percentLoadedText), with a line of text under the bar that says what is happening.
    ///
    /// The game shows that screen only from WorldGenerator.Start, in a chapter's scene. JoinScreen also shows it
    /// while a client joins, in the main menu's scene and over a world: every scene has a UI with the screen in it,
    /// hidden. The bar's textures may have been unloaded there (tk2d's UnloadTextures, Resources.UnloadAsset), so
    /// Show gives each sprite its texture again, the way WorldGenerator.Start does for the bar.
    ///
    /// For every load (single player, a host, a client), the line names the step the game's loading is on. It
    /// is worked out from WorldGenerator.percentLoaded, which the game raises after each step: the label shown is
    /// the step that runs next, since a long step holds up the frame and the screen shows what was set before it.
    /// </summary>
    public static class LoadingScreen
    {
        private const string LineName = "DwmpLoadingLine", HintName = "DwmpLoadingHint";

        private static UI _ui;            // the UI whose screen our text was added to
        private static tk2dTextMesh _line, _hint;
        private static bool _loggedTree;
        private static int _barFrame = -1;
        private static UI _barUi, _shownUi;
        private static WorldGenerator _stageWg;
        private static bool _stageLoading;
        private static string _shotKey;
        private static int _shotN;

        /// <summary>"--dwmp-load-shots DIR": a screenshot into DIR each time the line under the bar moves to a new step.</summary>
        public static string ShotsDir;

        /// <summary>The game's loading screen is up in this scene.</summary>
        public static bool Up
        {
            get
            {
                var ui = Singleton<UI>.Instance;
                return ui != null && ui.loadingScreen != null && ui.loadingScreen.activeInHierarchy;
            }
        }

        /// <summary>Show the game's loading screen in whatever scene this is (JoinScreen, every frame it needs it).</summary>
        public static void Show()
        {
            var ui = Singleton<UI>.Instance;
            if (ui == null || ui.loadingScreen == null) return;
            if (ui.loadingScreen.activeInHierarchy && ui == _shownUi) return;
            _shownUi = ui;
            if (!ui.loadingScreen.activeSelf) ui.loadingScreen.SetActive(true);
            // A screen the game has shown and hidden before faded every part to clear white (tweenColorInChildren).
            foreach (var s in ui.loadingScreen.GetComponentsInChildren<tk2dBaseSprite>(true))
            {
                if (s.color.a < 1f) s.color = new Color(s.color.r, s.color.g, s.color.b, 1f);
                var r = s.GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null && s.CollectionInst != null && s.spriteId >= 0 && s.spriteId < s.CollectionInst.spriteDefinitions.Length)
                    r.sharedMaterial.mainTexture = s.CollectionInst.spriteDefinitions[s.spriteId].materialInst.mainTexture;
            }
            foreach (var t in ui.loadingScreen.GetComponentsInChildren<tk2dTextMesh>(true))
                if (t.color.a < 1f) t.color = new Color(t.color.r, t.color.g, t.color.b, 1f);
            _barFrame = -1;
            Plugin.Log.LogInfo("[loading] showing the game's loading screen");
        }

        /// <summary>Take down a screen JoinScreen showed (joining failed before the world began to load).</summary>
        public static void Hide()
        {
            var ui = Singleton<UI>.Instance;
            SetLine(null, null);
            if (ui != null && ui.loadingScreen != null && WorldGenFinished()) ui.loadingScreen.SetActive(false);
            _shownUi = null;
        }

        private static bool WorldGenFinished() => Singleton<WorldGenerator>.Instance == null || Singleton<WorldGenerator>.Instance.finished;

        /// <summary>Fill the bar to this fraction (0..1), as WorldGenerator.percentLoaded's setter does: 51 frames, 0 to 50.</summary>
        public static void SetBar(float fraction, bool force = false)
        {
            var ui = Singleton<UI>.Instance;
            if (ui == null || ui.loadingBar == null) return;
            int pct = Mathf.Clamp(Mathf.FloorToInt(fraction * 100f), 0, 100);
            int frame = pct / 2;
            if (!force && frame == _barFrame && ui == _barUi) return;
            _barFrame = frame;
            _barUi = ui;
            try { ui.loadingBar.SetSprite("loadingBar_" + frame.ToString("00")); } catch { }
            if (ui.percentLoadedText != null) ui.percentLoadedText.text = pct + "%";
        }

        /// <summary>The line under the bar (null: none) and a smaller one under it (Esc: stop joining).</summary>
        public static void SetLine(string text, string hint)
        {
            var ui = Singleton<UI>.Instance;
            if (ui == null || ui.loadingScreen == null || ui.loadingBar == null) return;
            if (ui != _ui || _line == null || _hint == null) { _ui = ui; Make(ui); }
            if (_line == null) return;
            Set(_line, text);
            Set(_hint, hint);
            Shot(text);
        }

        private static void Shot(string text)
        {
            if (ShotsDir == null || string.IsNullOrEmpty(text)) return;
            int cut = text.IndexOfAny(new[] { '(', ':' });
            string key = cut > 0 ? text.Substring(0, cut) : text;
            if (key == _shotKey) return;
            _shotKey = key;
            try
            {
                System.IO.Directory.CreateDirectory(ShotsDir);
                string slug = new string(System.Array.FindAll(key.ToLowerInvariant().ToCharArray(), ch => char.IsLetterOrDigit(ch) || ch == ' ')).Trim().Replace(' ', '-');
                string path = System.IO.Path.Combine(ShotsDir, $"load-{++_shotN:00}-{slug}.png");
                ScreenCapture.CaptureScreenshot(path);
                Plugin.Log.LogInfo($"[loading] screenshot {path}: {text}");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[loading] screenshot failed: {e.Message}"); }
        }

        private static void Set(tk2dTextMesh t, string text)
        {
            if (t == null) return;
            bool on = !string.IsNullOrEmpty(text);
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            if (on && t.text != text) { t.text = text; t.Commit(); }
        }

        /// <summary>
        /// Our two lines: text meshes in the font of the screen's own tip text (the game's outlined Tahoma), children
        /// of the screen (so the game's fade-out takes them too). The message goes just below the bar and the
        /// hint under the message, placed from the bar's rendered bounds along the UI camera's "up": the screen is laid out on
        /// the X/Z plane and seen from above, so world Y is depth, not height. The bar's sprite is much taller
        /// than the thin line it draws (256 units for a line and its glow a little below its centre), so the
        /// lines sit at a share of that height from its centre, which keeps the gap the same at any resolution
        /// (the UI camera scales the whole screen with the window's height).
        /// </summary>
        private static void Make(UI ui)
        {
            if (!_loggedTree) { _loggedTree = true; LogTree(ui.loadingScreen.transform, 0); }
            var screen = ui.loadingScreen.transform;
            var barR = ui.loadingBar.GetComponent<Renderer>();
            var tip = screen.Find("Tip")?.GetComponent<tk2dTextMesh>() ?? ui.percentLoadedText;
            if (barR == null || tip == null) return;

            var cam = UiCamera(ui.loadingBar.gameObject.layer);
            Vector3 up = cam != null ? cam.transform.up : Vector3.forward;
            var b = barR.bounds;
            float height = Mathf.Abs(Vector3.Dot(b.size, up));
            if (height < 1f) height = 256f;
            _line = MakeText(screen, LineName, tip, b.center - up * (height * 0.28f), 1f, new Color(0.85f, 0.82f, 0.74f, 1f));
            _hint = MakeText(screen, HintName, tip, b.center - up * (height * 0.38f), 0.8f, new Color(0.55f, 0.52f, 0.45f, 1f));
            if (cam != null)
            {
                Vector3 c = cam.WorldToScreenPoint(b.center), top = cam.WorldToScreenPoint(b.center + up * height / 2f);
                Vector3 l = cam.WorldToScreenPoint(_line.transform.position), h = cam.WorldToScreenPoint(_hint.transform.position);
                Plugin.Log.LogInfo($"[loading] screen {Screen.width}x{Screen.height}: bar centre y={c.y:0} (sprite top {top.y:0}), message y={l.y:0}, hint y={h.y:0}");
            }
        }

        /// <summary>The camera that draws this layer (the UI's), if there is one.</summary>
        private static Camera UiCamera(int layer)
        {
            foreach (var c in Camera.allCameras)
                if ((c.cullingMask & (1 << layer)) != 0 && c.orthographic) return c;
            return null;
        }

        private static tk2dTextMesh MakeText(Transform screen, string name, tk2dTextMesh like, Vector3 worldPos, float size, Color color)
        {
            var old = screen.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(name);
            go.layer = like.gameObject.layer;
            go.transform.SetParent(screen, false);
            go.transform.position = worldPos;
            go.transform.rotation = like.transform.rotation;
            go.transform.localScale = Vector3.one;
            var t = go.AddComponent<tk2dTextMesh>();
            t.font = like.font;
            t.maxChars = 160;
            t.anchor = TextAnchor.MiddleCenter;
            // The same size on screen as the tip, whatever the two transforms' scales are.
            var ls = like.transform.lossyScale; var ps = screen.lossyScale;
            t.scale = new Vector3(like.scale.x * ls.x / ps.x, like.scale.y * ls.y / ps.y, 1f) * size;
            t.color = color;
            t.text = "";
            t.Commit();
            var r = go.GetComponent<MeshRenderer>();
            var lr = like.GetComponent<MeshRenderer>();
            if (r != null && lr != null) { r.sortingLayerID = lr.sortingLayerID; r.sortingOrder = lr.sortingOrder; }
            go.SetActive(false);
            return t;
        }

        private static void LogTree(Transform t, int depth)
        {
            if (depth > 4) return;
            var tm = t.GetComponent<tk2dTextMesh>();
            var sp = t.GetComponent<tk2dBaseSprite>();
            var r = t.GetComponent<Renderer>();
            Plugin.Log.LogInfo($"[loading] {new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} pos={t.position} scale={t.lossyScale}"
                + (tm != null ? $" text=\"{tm.text}\" font={tm.font?.name} tscale={tm.scale}" : "")
                + (sp != null ? $" sprite={sp.CurrentSprite?.name}" : "")
                + (r != null ? $" bounds={r.bounds.center}/{r.bounds.size} order={r.sortingOrder}" : ""));
            foreach (Transform c in t) LogTree(c, depth + 1);
        }

        // ---- the game's own loading steps -------------------------------------

        /// <summary>What the game's loading is doing now, or null when no world is loading.</summary>
        public static string GameStage()
        {
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg == null) return null;
            if (wg.finished) return null; // done: the screen is fading out (or is up for a location, OutsideLocations)
            float p = wg.percentLoaded;
            if (wg != _stageWg) { _stageWg = wg; _stageLoading = false; }
            if (Core.loadingGame) _stageLoading = true; // it goes false again just before Load's last step
            if (_stageLoading) // SaveManager.Load
            {
                if (p <= 0f) return "Reading the save...";
                if (p < 50f) return $"Placing the saved objects ({Mathf.FloorToInt(p * 2f)}%)...";
                if (p < 70f) return "Fitting the objects together...";
                if (p < 75f) return "Restoring dreams, the map and events...";
                if (p < 80f) return "Restoring the state of the world...";
                if (p < 85f) return "Setting up places and characters...";
                return "Showing the area around you...";
            }
            // WorldGenerator.generateWorld: a new game, or a chapter made from its chapter save
            if (p < 20f) return "Laying out the forest...";
            if (p < 30f) return "Filling the forest with places...";
            if (p < 40f) return "Building the roads...";
            if (p < 70f) return "Growing trees and placing objects...";
            if (p < 72f) return "Placing you in the world...";
            if (p < 92f) return "Working out paths through the forest...";
            return "Bringing in characters and items...";
        }

        private static UI _tipUi;
        private static Renderer _tip;

        /// <summary>
        /// The game's tip along the top of the loading screen ("You are playing a challenging and unforgiving game.")
        /// isn't drawn: the line under the bar says what's happening instead. The text mesh stays (our lines take its
        /// font), only its renderer is off.
        /// </summary>
        private static void HideTip()
        {
            var ui = Singleton<UI>.Instance;
            if (ui == null || ui.loadingScreen == null || !ui.loadingScreen.activeInHierarchy) return;
            if (ui != _tipUi) { _tipUi = ui; _tip = ui.loadingScreen.transform.Find("Tip")?.GetComponent<Renderer>(); }
            if (_tip != null && _tip.enabled) _tip.enabled = false;
        }

        /// <summary>Every frame: in a load the game runs by itself, write its step under its bar.</summary>
        public static void Update()
        {
            HideTip();
            if (JoinScreen.Shown) return; // JoinScreen writes its own line
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg == null || !Up) { if (_line != null && _ui == Singleton<UI>.Instance && _line.gameObject.activeSelf) SetLine(null, null); return; }
            SetLine(GameStage(), null);
        }
    }

    /// <summary>The game set its bar: while a client joins, JoinScreen's bar (download first, then this) wins.</summary>
    [HarmonyPatch(typeof(WorldGenerator), "set_percentLoaded")]
    static class PercentLoadedPatch
    {
        static void Postfix() => JoinScreen.OnGameBar();
    }
}
