using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Client: one loading screen, the game's own (LoadingScreen), from the moment we take the host's world (its
    /// SaveOffer, or Welcome when we already had it) until the host's world is playable. The line under the bar
    /// says what is happening: connecting, downloading with the MB so far, waiting for the host, then the steps
    /// of the game's own loading. One bar for the whole join: the download fills the first DownloadShare of it,
    /// the game's loading (WorldGenerator.percentLoaded) the rest, and it never goes back.
    ///
    /// The game would show its screen only once the chapter's scene has loaded, so until then this shows it in
    /// whatever scene there is: the main menu's, the world a client joins from the pause menu, and the main menu
    /// again after leaving that world. It keeps the game's black fades (UI.blackScreenTop, in initNewGame and
    /// WorldGenerator.Start) clear while it's up, so the change of scene doesn't dip to black.
    ///
    /// If joining fails before then (refused, a bad download, the connection dropped) the screen goes away and
    /// the co-op panel opens, which says why (WorldShare.LoadStatus). Esc stops joining until the world starts
    /// loading. The game's menu buttons are off while it's up.
    /// </summary>
    public static class JoinScreen
    {
        private enum Stage { None, Connecting, Downloading, Waiting, Loading, Game }

        private const float DownloadShare = 0.3f;

        private static Stage _stage;
        private static bool _shown;
        private static float _shownAt; // when the screen came up, for the stage times in the log
        private static string At => $" (at {Time.realtimeSinceStartup - _shownAt:0.0} s)";
        private static bool _inGame;             // the chapter's scene is up and its WorldGenerator runs the screen
        private static float _fraction;          // of the whole join, 0..1, never lower than before
        private static string _line;
        private static int _menuScene;           // the scene StartProfile was called in
        private static float _handOverUntil;     // give up waiting for the game's loading screen at this time
        private static Controller _buttonsOffOn; // the Controller whose buttons we turned off, to turn them back on

        public static bool Shown => _shown;

        /// <summary>We took the host's world: show the screen (once; a second offer keeps it up).</summary>
        public static void Begin()
        {
            if (!_shown)
            {
                _shown = true;
                _inGame = false;
                _fraction = 0f;
                _shownAt = Time.realtimeSinceStartup;
                Plugin.Log.LogInfo("[join] loading screen shown");
                Plugin.Instance?.SetPanel(false);
            }
            if (_stage == Stage.Game) { _stage = Stage.None; _inGame = false; }
            LoadingScreen.Show();
        }

        /// <summary>UI.initLoadGame has been started: the game's loading takes over the rest of the bar.</summary>
        public static void HandOver()
        {
            if (!_shown) return;
            _menuScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            _handOverUntil = Time.realtimeSinceStartup + 30f;
            _buttonsOffOn = null; // initNewGame turns the buttons off itself, and the next scene has its own Controller
            SetStage(Stage.Game, WorldShare.Instance?.LoadStatus ?? "Loading the host's world...");
        }

        /// <summary>Joining stopped before the world loaded: take the screen down and open the panel, which says why.</summary>
        public static void Fail(string why)
        {
            if (!_shown) return;
            if (_buttonsOffOn != null && _buttonsOffOn == Singleton<Controller>.Instance) _buttonsOffOn.buttonsDisabled = false;
            _buttonsOffOn = null;
            Plugin.Log.LogInfo($"[join] loading screen hidden: {why}");
            Clear();
            LoadingScreen.Hide();
            Plugin.Instance?.OpenPanel();
        }

        private static void Clear()
        {
            _shown = false;
            _inGame = false;
            _stage = Stage.None;
            _line = null;
        }

        /// <summary>Every frame (Plugin.Update): work out the stage from the session, the download and the game's loading.</summary>
        public static void Update(Session session, WorldShare world)
        {
            if (!_shown) return;

            if (_stage == Stage.Game)
            {
                var wg = Singleton<WorldGenerator>.Instance;
                bool newScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != _menuScene;
                if (!_inGame && newScene && wg != null && LoadingScreen.Up)
                {
                    _inGame = true;
                    Plugin.Log.LogInfo("[join] loading screen: the game's loading has it now" + At);
                }
                if (_inGame && !LoadingScreen.Up)
                {
                    Plugin.Log.LogInfo("[join] loading screen: the host's world is up" + At);
                    Clear();
                    return;
                }
                if (!_inGame && Time.realtimeSinceStartup > _handOverUntil)
                {
                    Plugin.Log.LogInfo("[join] loading screen: the game's loading didn't start, hiding it");
                    Clear();
                    LoadingScreen.Hide();
                    return;
                }
                if (!_inGame) LoadingScreen.Show(); // the menu's scene, until the chapter's loads
                if (_inGame && wg != null) Raise(DownloadShare + (1f - DownloadShare) * Mathf.Clamp01(wg.percentLoaded / 100f));
                _line = _inGame ? (LoadingScreen.GameStage() ?? _line) : (world.LoadStatus ?? _line);
                Draw();
                return;
            }

            if (session.IsHost || !session.Active)
            {
                Fail(world.LoadStatus ?? session.Status);
                return;
            }

            LoadingScreen.Show(); // again after a change of scene (leaving our own world for the main menu)

            // Under the screen, the menu mustn't take clicks or Esc (InputScript.onPressEsc does nothing while
            // the buttons are off).
            var c = Singleton<Controller>.Instance;
            if (Core.mainMenu && c != null && !c.buttonsDisabled) { c.buttonsDisabled = true; _buttonsOffOn = c; }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                world.SetLoadStatus("You stopped joining.");
                session.Backend?.Leave();
                Fail("stopped with Esc");
                return;
            }

            if (world.Progress is var p && p.HasValue)
            {
                Raise(DownloadShare * (p.Value.Total > 0 ? (float)p.Value.Got / p.Value.Total : 0f));
                SetStage(Stage.Downloading, $"Downloading the host's world: {p.Value.Got / 1048576f:0.0} / {p.Value.Total / 1048576f:0.0} MB" +
                    (world.UnpackedSize > 0 ? $" ({world.UnpackedSize / 1048576f:0.0} MB unpacked)" : ""));
            }
            else if (session.Connected)
            {
                Raise(DownloadShare);
                SetStage(Stage.Loading, world.LoadStatus ?? "Loading the host's world...");
            }
            else if (world.LoadStatus != null)
                SetStage(Stage.Waiting, world.LoadStatus); // JoinWait: the host can't let us in yet, and says why
            else if (_fraction > 0f)
                SetStage(Stage.Waiting, "Waiting for the host to let you in..."); // downloaded; Hello sent again with its hash
            else
                SetStage(Stage.Connecting, "Connecting to the host...");
            Draw();
        }

        /// <summary>After the game's code each frame: keep its black fades from covering the screen while we're joining.</summary>
        public static void LateUpdate()
        {
            if (!_shown) return;
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg != null && wg.finished) return;
            var ui = Singleton<UI>.Instance;
            if (ui == null || ui.blackScreenTop == null || !ui.blackScreenTop.activeInHierarchy) return;
            var s = ui.blackScreenTop.GetComponent<tk2dSprite>();
            if (s != null && s.color.a > 0f) s.color = new Color(s.color.r, s.color.g, s.color.b, 0f);
        }

        /// <summary>WorldGenerator.percentLoaded was set (it sets the bar's sprite): put the join's bar back.</summary>
        public static void OnGameBar()
        {
            if (!_shown || !_inGame) return;
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg != null) Raise(DownloadShare + (1f - DownloadShare) * Mathf.Clamp01(wg.percentLoaded / 100f));
            LoadingScreen.SetBar(_fraction, force: true);
        }

        private static void Raise(float f) { if (f > _fraction) _fraction = Mathf.Clamp01(f); }

        private static void Draw()
        {
            LoadingScreen.SetBar(_fraction);
            LoadingScreen.SetLine(_line, _stage != Stage.Game ? "Esc: stop joining" : null);
        }

        private static void SetStage(Stage s, string line)
        {
            _line = line;
            if (s == _stage) return;
            _stage = s;
            Plugin.Log.LogInfo($"[join] loading screen: {s.ToString().ToLowerInvariant()} {Mathf.RoundToInt(_fraction * 100f)}%" + At);
        }
    }
}
